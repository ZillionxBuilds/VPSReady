using System.Text.Json;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwStoredSshTests
{
    [Theory]
    [InlineData(22, true, false)]
    [InlineData(2222, true, true)]
    [InlineData(65535, false, false)]
    public async Task CompleteStoredEvidenceSurvivesOnlyAsBoundedTypedFacts(int port, bool ipv6, bool session6)
    {
        var wire = StoredUfwFixture.Create(port, ipv6: ipv6, session6: session6);
        var result = await Capture(wire);
        Assert.True(result.StoredSshEvidence!.HasRequiredAllows);
        Assert.Equal(port, result.StoredSshEvidence.ServerPort);
        Assert.Equal(ipv6, result.StoredSshEvidence.Ipv6Enabled);
        Assert.Equal(session6, result.StoredSshEvidence.SessionIsIpv6);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.DoesNotContain("ufw-user", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("ufw-user", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void MissingEitherRequiredFamilyNeverProvesSafety(bool allow4, bool allow6) =>
        Assert.False(UfwStoredSshParser.Parse(StoredUfwFixture.Create(allow4: allow4, allow6: allow6))!.HasRequiredAllows);

    [Theory]
    [InlineData("-A ufw-user-input -p tcp --dport 22 -j DROP\n")]
    [InlineData("-A ufw-user-input -p tcp --dport 22 -j REJECT\n")]
    [InlineData("-A ufw-user-input -p tcp --dport 22 -j ufw-user-limit\n")]
    [InlineData("-A ufw-user-input -i eth0 -p tcp --dport 22 -j ACCEPT\n")]
    [InlineData("-A ufw-user-input -p tcp --dport 65536 -j ACCEPT\n")]
    public void ConflictAndUnknownSyntaxFailClosedEvenWithLaterAllow(string rule) =>
        Assert.Null(UfwStoredSshParser.Parse(StoredUfwFixture.Create(rules4: rule + StoredUfwFixture.Allow(false, 22))));

    [Theory]
    [InlineData("-A ufw-user-input -p tcp --dport 22 -s 192.0.2.0/24 -j ACCEPT\n")]
    [InlineData("-A ufw-user-input -p tcp -d 192.0.2.1 --dport 22 -j ACCEPT\n")]
    [InlineData("-A ufw-user-input -p udp --dport 22 -j ACCEPT\n")]
    [InlineData("-A ufw-user-output -p tcp --dport 22 -j ACCEPT\n")]
    public void RestrictedOrWrongDirectionProtocolDoesNotProveBroadSshAllow(string rule) =>
        Assert.False(UfwStoredSshParser.Parse(StoredUfwFixture.Create(rules4: rule))!.HasRequiredAllows);

    [Theory]
    [InlineData("before4=")]
    [InlineData("after6=")]
    [InlineData("sysctl=")]
    [InlineData("before_init=")]
    [InlineData("output=")]
    public void ChangedFrameworkOrPolicyIsUnsupported(string field) =>
        Assert.Null(UfwStoredSshParser.Parse(StoredUfwFixture.Create().Replace(field, field + "unknown", StringComparison.Ordinal)));

    [Fact]
    public async Task IncompleteOversizedNonzeroAndDisplayOnlyEvidenceIsNeverAccepted()
    {
        var valid = StoredUfwFixture.Create();
        Assert.Null((await Capture(valid.Replace("[end]", "", StringComparison.Ordinal))).StoredSshEvidence);
        Assert.Null((await Capture(valid + new string(' ', UfwStoredSshParser.MaximumBytes))).StoredSshEvidence);
        Assert.Null((await Capture(valid, 77)).StoredSshEvidence);
        Assert.Null((await Capture("Added user rules (see 'ufw status' for running firewall):\nufw allow 22/tcp")).StoredSshEvidence);
        Assert.False(UfwStoredSshParser.Parse(StoredUfwFixture.Create(ipv6: false, session6: true))!.HasRequiredAllows);
    }

    private static Task<RemoteCommandResult> Capture(string wire, int exit = 0) => ProductionOutput.CaptureAsync(UfwStoredSshCommand.Create(), new(exit, wire, "synthetic-private-detail", TimeSpan.Zero), CancellationToken.None);

    // Optional fixture-backed tests are explicitly skipped without extracted
    // public Ubuntu packages. No installed UFW, privileges or VPS are used.
    [UfwPackageTheory]
    [InlineData("jammy", true, 22)]
    [InlineData("jammy", false, 2222)]
    [InlineData("noble", true, 2222)]
    [InlineData("noble", false, 22)]
    public async Task UbuntuPackageTemplatesAndProductionInspectionAgree(string suite, bool root, int port)
    {
        using var sandbox = new ShellSandbox();
        var fixture = Path.Combine(Environment.GetEnvironmentVariable("VPSREADY_UFW_PACKAGE_FIXTURES")!, suite);
        var config = Path.Combine(sandbox.Root, "ufw");
        Directory.CreateDirectory(config);
        foreach (var file in new[] { "before.rules", "after.rules", "before6.rules", "after6.rules", "user.rules", "user6.rules" })
        {
            File.Copy(Path.Combine(fixture, "usr/share/ufw/iptables", file), Path.Combine(config, file));
        }
        foreach (var file in new[] { "before.init", "after.init" }) { File.Copy(Path.Combine(fixture, "usr/share/ufw", file), Path.Combine(config, file)); }
        File.Copy(Path.Combine(fixture, "etc/ufw/sysctl.conf"), Path.Combine(config, "sysctl.conf"));
        var defaults = Path.Combine(sandbox.Root, "ufw-defaults");
        await File.WriteAllTextAsync(defaults, (await File.ReadAllTextAsync(Path.Combine(fixture, "etc/default/ufw"))).Replace("/etc/ufw/", config + "/", StringComparison.Ordinal));
        sandbox.Environment["SSH_CONNECTION"] = $"192.0.2.1 54321 192.0.2.2 {port}";
        sandbox.Stub("id", root ? "printf '0\\n'" : "printf '1000\\n'");
        sandbox.Stub("sudo", "[ \"$1\" = -n ] || exit 77; shift; exec env -i PATH=\"$PATH\" \"$@\"");
        if (OperatingSystem.IsMacOS()) { sandbox.Stub("sha256sum", "exec /usr/bin/shasum -a 256"); }
        // Only redirect fixture file paths; production inspection remains intact.
        var script = UfwStoredSshCommand.ShellCommand.Replace("/etc/ufw/", config + "/", StringComparison.Ordinal).Replace("/etc/default/ufw", defaults, StringComparison.Ordinal);
        var changedFramework = false;
        async Task<UfwStoredSshEvidence?> Inspect()
        {
            var wire = await sandbox.RunAsync(script);
            Assert.Equal(0, wire.ExitCode);
            if (!changedFramework)
            {
                var expectedHeader = StoredUfwFixture.Create(port).Split("[user4]", StringSplitOptions.None)[0]
                    .Replace(UfwStoredSshParser.Sysctl, UfwStoredSshParser.UbuntuSysctl, StringComparison.Ordinal)
                    .Replace("before_init=absent", "before_init=" + UfwStoredSshParser.BeforeInit, StringComparison.Ordinal)
                    .Replace("after_init=absent", "after_init=" + UfwStoredSshParser.AfterInit, StringComparison.Ordinal);
                Assert.Equal(expectedHeader, wire.Output.Split("[user4]", StringSplitOptions.None)[0]);
            }
            return (await Capture(wire.Output)).StoredSshEvidence;
        }
        Assert.False((await Inspect())!.HasRequiredAllows);
        foreach (var (file, ipv6) in new[] { ("user.rules", false), ("user6.rules", true) })
        {
            var path = Path.Combine(config, file);
            var saved = Path.Combine(Environment.GetEnvironmentVariable("VPSREADY_UFW_PACKAGE_FIXTURES")!, "generated", $"{(ipv6 ? 6 : 4)}-{port}-low-1.rules");
            File.Copy(saved, path, overwrite: true);
            if (!ipv6) { Assert.False((await Inspect())!.HasRequiredAllows); }
        }
        Assert.True((await Inspect())!.HasRequiredAllows);
        // Every generated logging/capability profile goes through the same
        // production file command and bounded parser, never typed fake success.
        foreach (var level in new[] { "off", "low", "medium", "high", "full" })
        {
            foreach (var limit in new[] { 0, 1 })
            {
                foreach (var family in new[] { 4, 6 })
                {
                    File.Copy(Path.Combine(Environment.GetEnvironmentVariable("VPSREADY_UFW_PACKAGE_FIXTURES")!, "generated", $"{family}-{port}-{level}-{limit}.rules"), Path.Combine(config, family == 4 ? "user.rules" : "user6.rules"), overwrite: true);
                }
                Assert.True((await Inspect())!.HasRequiredAllows);
            }
        }
        await File.AppendAllTextAsync(Path.Combine(config, "before.rules"), "\n-A ufw-before-input -p tcp -j DROP\n");
        changedFramework = true;
        Assert.Null(await Inspect());
        sandbox.Stub("id", "printf '1000\\n'");
        sandbox.Stub("sudo", "exit 1");
        Assert.Equal(77, (await sandbox.RunAsync(script)).ExitCode);
    }
}

public sealed class UfwPackageTheoryAttribute : TheoryAttribute
{
    public UfwPackageTheoryAttribute()
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VPSREADY_UFW_PACKAGE_FIXTURES")))
        {
            Skip = "Offline Ubuntu package inspection NOT RUN: supply extracted, checksum-verified public packages via VPSREADY_UFW_PACKAGE_FIXTURES on a POSIX host.";
        }
    }
}
