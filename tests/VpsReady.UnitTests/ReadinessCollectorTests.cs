using System.Text;
using System.Text.Json;
using System.IO.Compression;
using VpsReady.Application;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ReadinessCollectorTests
{
    [Fact]
    public async Task CompleteInspectionUsesAllNineRequiredAndFreshDisposedIndependentLogin()
    {
        await using var host = new ReadOnlyFixtureHost();
        var sink = new Recorder();
        var snapshot = await Collect(host, sink);
        Assert.Equal(ReadinessExecution.Completed, snapshot.Execution);
        Assert.All(snapshot.Rows.Take(9), row => Assert.Equal(ReadinessCheckState.Pass, row.State));
        Assert.Equal(ReadinessVerdict.ReadyWithWarnings, Verdict(snapshot));
        Assert.Equal(ReadinessReason.CacheFreshnessUnknown, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.A02).Reason);
        Assert.Equal(1, host.LoginAttempts);
        Assert.Equal(1, host.ProbeDisposals);
        Assert.Equal(0, host.MainDisposals);
        Assert.Equal("configuration-original", host.ConfigurationDigest);
        Assert.Equal(11, host.Commands.Count); // Main channel; the twelfth command is the separate login minimum.
        Assert.All(host.Commands, command => Assert.True(UbuntuReadinessCommandCatalog.Supports(command)
            || command is RemoteCommandCatalog.SshConnectionTest or RemoteCommandCatalog.UbuntuUfwStoredSshRead));
        Assert.All(sink.Events, entry => Assert.True(DiagnosticEventCatalog.IsKnown(entry.EventId)));
        Assert.All(sink.Events.Where(entry => entry.ErrorCode is not null), entry => Assert.True(DiagnosticErrorCatalog.IsKnown(entry.ErrorCode!)));
        var journal = JsonSerializer.Serialize(sink.Events);
        Assert.DoesNotContain("fixture-private-host", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("Asia/Bangkok", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("ufw-user", journal, StringComparison.Ordinal);
        Assert.DoesNotContain(DiagnosticEventCatalog.ReadinessCompleted, journal, StringComparison.Ordinal); // Session owns terminal.
    }

    [Theory]
    [InlineData(RemoteTransportFailureKind.Authentication, ReadinessCheckState.Fail, ReadinessReason.AuthenticationRejected)]
    [InlineData(RemoteTransportFailureKind.HostTrust, ReadinessCheckState.Fail, ReadinessReason.HostTrustFailed)]
    [InlineData(RemoteTransportFailureKind.Network, ReadinessCheckState.Unknown, ReadinessReason.ProbeFailed)]
    [InlineData(RemoteTransportFailureKind.Timeout, ReadinessCheckState.Unknown, ReadinessReason.ProbeTimeout)]
    [InlineData(RemoteTransportFailureKind.KeyIdentity, ReadinessCheckState.Unknown, ReadinessReason.CredentialUnavailable)]
    public async Task FreshLoginFailureNeverRetriesFallsBackOrClaimsOldChannelAsFresh(RemoteTransportFailureKind fault,
        ReadinessCheckState expected, ReadinessReason reason)
    {
        await using var host = new ReadOnlyFixtureHost { LoginFailure = fault };
        var snapshot = await Collect(host, new Recorder());
        var row = snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R03);
        Assert.Equal(expected, row.State);
        Assert.Equal(reason, row.Reason);
        Assert.Equal(1, host.LoginAttempts);
        Assert.NotEqual(ReadinessVerdict.ReadyWithWarnings, Verdict(snapshot));
        Assert.NotEqual(ReadinessCheckState.Pass, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R06).State);
        Assert.Equal(0, host.MainDisposals);
    }

    [Fact]
    public async Task MissingCredentialCannotAuthenticateFromSurvivingChannel()
    {
        await using var host = new ReadOnlyFixtureHost { CanReauthenticate = false };
        var snapshot = await Collect(host, new Recorder());
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(snapshot));
        Assert.Equal(0, host.LoginAttempts);
        Assert.Equal(ReadinessCheckState.Pass, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R02).State);
        Assert.Equal(ReadinessReason.CredentialUnavailable, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R03).Reason);
    }

    [Fact]
    public async Task PasswordModeIsAdvisoryOnlyAndUsesItsOwnFreshLogin()
    {
        await using var host = new ReadOnlyFixtureHost { AuthenticationMode = SshAuthenticationMode.Password };
        var snapshot = await Collect(host, new Recorder());
        Assert.Equal(ReadinessVerdict.ReadyWithWarnings, Verdict(snapshot));
        Assert.Equal(ReadinessReason.PasswordModeAdvice, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.A01).Reason);
        Assert.Equal(1, host.LoginAttempts);
    }

    [Theory]
    [InlineData(RemoteCommandCatalog.ReadinessUfwRead)]
    [InlineData(RemoteCommandCatalog.UbuntuUfwStoredSshRead)]
    [InlineData(RemoteCommandCatalog.ReadinessAuditRead)]
    public async Task SudoTrueWithoutActualPrivilegedReadsCannotProveReadCapability(string denied)
    {
        await using var host = new ReadOnlyFixtureHost { DeniedCommand = denied };
        var snapshot = await Collect(host, new Recorder());
        Assert.Equal(ReadinessCheckState.Unknown, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R04).State);
        Assert.Equal(ReadinessReason.InsufficientReadPrivilege, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R04).Reason);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(snapshot));
    }

    [Fact]
    public async Task NonUbuntuIsPositiveUnsupportedWhileUnreadableNeverMeansUnsupported()
    {
        await using var host = new ReadOnlyFixtureHost { Platform = "ID=debian\nVERSION_ID=12\n" };
        var snapshot = await Collect(host, new Recorder());
        Assert.Equal(ReadinessVerdict.UnsupportedProfile, Verdict(snapshot));
        Assert.Equal(2, host.Commands.Count);
        host.Platform = "unreadable";
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(await Collect(host, new Recorder())));
    }

    [Fact]
    public async Task JournalFailureStopsWithoutCertifyingCompleteAndCancellationRetainsOnlyPartialEvidence()
    {
        await using var host = new ReadOnlyFixtureHost();
        var failed = await Collect(host, new Recorder { FailAt = 5 });
        Assert.Equal(ReadinessExecution.DiagnosticsFailed, failed.Execution);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(failed));
        Assert.Contains(failed.Rows, row => row.State == ReadinessCheckState.NotRun);
        using var cancellation = new CancellationTokenSource();
        var cancelled = await new UbuntuReadinessCollector(new Recorder()).CollectAsync(host, "ses_fixture", 1,
            Correlation(), row => { if (row.Id == ReadinessCheckId.R01) { cancellation.Cancel(); } }, cancellation.Token);
        Assert.Equal(ReadinessExecution.Cancelled, cancelled.Execution);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(cancelled));
    }

    [Fact]
    public async Task RestrictedSshRuleIsUnknownWithoutChangingSourceToAnywhere()
    {
        await using var host = new ReadOnlyFixtureHost
        {
            Stored = StoredUfwFixture.Create(rules4:
            "-A ufw-user-input -p tcp --dport 22 -s 192.0.2.0/24 -j ACCEPT\n")
        };
        var snapshot = await Collect(host, new Recorder());
        Assert.Equal(ReadinessCheckState.Unknown, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R06).State);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(snapshot));
        Assert.Equal("configuration-original", host.ConfigurationDigest);
    }

    [Fact]
    public async Task FreshLoginCannotPassWhenCleanDisposalExceedsWholeLoginBudget()
    {
        var clock = new Clock();
        await using var host = new ReadOnlyFixtureHost { AfterProbeDispose = () => clock.Timestamp = 21 };
        var snapshot = await new UbuntuReadinessCollector(new Recorder(), clock).CollectAsync(host, "ses_fixture", 1,
            Correlation(), null, CancellationToken.None);
        var login = snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R03);
        Assert.Equal(ReadinessCheckState.Unknown, login.State);
        Assert.Equal(ReadinessReason.ProbeTimeout, login.Reason);
        Assert.Equal(ReadinessVerdict.Incomplete, ReadinessEvaluator.Evaluate(snapshot, true, "ses_fixture", 1, clock));
        Assert.Equal(1, host.ProbeDisposals);
    }

    [Fact]
    public async Task MixedReadinessExportsRetainTypedFindingsAndCorrelationWithoutSeededPrivateData()
    {
        var root = Path.Combine(Path.GetTempPath(), "VpsReady.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var seeds = new[] { "fixture-private-host", "fixture-private-user", "/private/fixture/key-A", "disposable-readiness-unlock" };
            var redactor = new FailClosedRedactor();
            foreach (var seed in seeds) { redactor.RegisterSensitiveValue(seed); }
            using var journal = new OperationJournalWorkspace(new ExportPaths(root), redactor, new ExportClock(),
                new DiagnosticEnvironment("0.1.0-test", "vp123-review", "test-os", "test-arch"), new ExportFolderOpener());
            var sink = new RedactingDiagnosticSink(redactor, journal);
            await using var session = new ApplicationSession();
            await session.StartAsync(new(seeds[0], 22, seeds[1]), new ReadOnlyFixtureHost());
            using var vm = new ReadinessViewModel(session, new UbuntuReadinessCollector(sink), sink, _ => { });
            await vm.CheckAsync();
            Assert.Equal(ReadinessVerdict.ReadyWithWarnings, vm.Verdict);
            var report = journal.CreateSafeIssueReport(vm.RunId, vm.OperationId);
            Assert.Contains("core-basic-v1 v1.0", report, StringComparison.Ordinal);
            Assert.Contains("R03: Pass", report, StringComparison.Ordinal);
            Assert.Contains("A02: Warn", report, StringComparison.Ordinal);
            Assert.Contains("upstream freshness NOT PROVED", report, StringComparison.Ordinal);
            Assert.Contains(vm.OperationId!, report, StringComparison.Ordinal);
            Assert.Contains(vm.RunId!, report, StringComparison.Ordinal);
            var bundle = await journal.ExportSanitizedSupportBundleAsync(vm.RunId, Path.Combine(root, "exports"), CancellationToken.None, vm.OperationId);
            using var archive = ZipFile.OpenRead(bundle.BundlePath);
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = await reader.ReadToEndAsync();
                foreach (var seed in seeds) { Assert.DoesNotContain(seed, text, StringComparison.Ordinal); }
                Assert.DoesNotContain("Asia/Bangkok", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ufw-user", text, StringComparison.Ordinal);
            }
            foreach (var seed in seeds) { Assert.DoesNotContain(seed, report, StringComparison.Ordinal); }
            foreach (var activity in journal.GetActivity())
            {
                foreach (var seed in seeds) { Assert.DoesNotContain(seed, activity.Message, StringComparison.Ordinal); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ExportPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);
        public string GetDirectory(LocalStorageArea area) => Path.Combine(root, area.ToString().ToLowerInvariant());
        public string ResolvePath(LocalStorageArea area, string relativePath) => Path.Combine(GetDirectory(area), relativePath);
    }

    private sealed class ExportClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
    private sealed class ExportFolderOpener : IDiagnosticFolderOpener
    {
        public Task OpenAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Timestamp;
    }

    private static CorrelationIds Correlation() => new("ses_fixture", "run_fixture", "op_fixture", "validate");
    private static Task<ReadinessSnapshot> Collect(ReadOnlyFixtureHost host, Recorder sink) => new UbuntuReadinessCollector(sink)
        .CollectAsync(host, "ses_fixture", 1, Correlation(), null, CancellationToken.None);
    private static ReadinessVerdict Verdict(ReadinessSnapshot snapshot) => ReadinessEvaluator.Evaluate(snapshot, true, "ses_fixture", 1, TimeProvider.System);

    private sealed class Recorder : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public int? FailAt { get; init; }
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            if (Events.Count + 1 == FailAt) { throw new IOException("fixture journal unavailable"); }
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ReadOnlyFixtureHost : IAuthenticatedSessionTransport
    {
        public SshAuthenticationMode AuthenticationMode { get; init; } = SshAuthenticationMode.PrivateKey;
        public string? KeyFingerprint => AuthenticationMode == SshAuthenticationMode.PrivateKey ? "SHA256:fixture" : null;
        public bool CanReauthenticate { get; init; } = true;
        public string Platform { get; set; } = "ID=ubuntu\nVERSION_ID=24.04\n";
        public string Stored { get; init; } = StoredUfwFixture.Create();
        public string? DeniedCommand { get; init; }
        public RemoteTransportFailureKind? LoginFailure { get; init; }
        public int LoginAttempts { get; private set; }
        public int ProbeDisposals { get; private set; }
        public int MainDisposals { get; private set; }
        public Action? AfterProbeDispose { get; init; }
        public List<string> Commands { get; } = [];
        public string ConfigurationDigest { get; } = "configuration-original";
        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command.Id.Value);
            if (command.Id.Value == DeniedCommand) { return new(77, "", "", TimeSpan.Zero); }
            var output = command.Id.Value switch
            {
                RemoteCommandCatalog.ReadinessPlatformRead => Platform,
                RemoteCommandCatalog.SshConnectionTest => "",
                RemoteCommandCatalog.ReadinessPrivilegeRead => "root=false\nsudo=available\n",
                RemoteCommandCatalog.ReadinessUfwRead => "Status: active\nTo                         Action      From\n--                         ------      ----\n",
                RemoteCommandCatalog.UbuntuUfwStoredSshRead => Stored,
                RemoteCommandCatalog.ReadinessAuditRead => "\nreadiness-audit-complete\n",
                RemoteCommandCatalog.ReadinessDiskRead => "10737418240 5368709120 rw,relatime\n",
                RemoteCommandCatalog.ReadinessRebootRead => "reboot=false\nreadiness-reboot-complete\n",
                RemoteCommandCatalog.ReadinessCachedUpgradeRead => "0 upgraded, 0 newly installed, 0 to remove and 0 not upgraded.\n",
                RemoteCommandCatalog.ReadinessIdentityRead => "fixture-private-host\nAsia/Bangkok\nreadiness-identity-complete\n",
                RemoteCommandCatalog.ReadinessTimeSyncRead => "yes\n",
                _ => throw new InvalidOperationException("Unknown fixture command ID."),
            };
            return await SshNetBoundedOutputCapture.ReadResultAsync(command, 0, new MemoryStream(Encoding.UTF8.GetBytes(output)),
                new MemoryStream(), TimeSpan.Zero, cancellationToken);
        }
        public Task<IRemoteTransport> CreateAuthenticatedProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoginAttempts++;
            if (LoginFailure is { } kind) { throw new RemoteTransportException(kind); }
            return Task.FromResult<IRemoteTransport>(new Probe(this));
        }
        public ValueTask DisposeAsync() { MainDisposals++; return ValueTask.CompletedTask; }
        private sealed class Probe(ReadOnlyFixtureHost owner) : IRemoteTransport
        {
            public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
                command.Id.Value == RemoteCommandCatalog.SshConnectionTest ? Task.FromResult(new RemoteCommandResult(0, "", "", TimeSpan.Zero))
                : throw new InvalidOperationException("Unexpected probe command ID.");
            public ValueTask DisposeAsync() { owner.ProbeDisposals++; owner.AfterProbeDispose?.Invoke(); return ValueTask.CompletedTask; }
        }
    }
}
