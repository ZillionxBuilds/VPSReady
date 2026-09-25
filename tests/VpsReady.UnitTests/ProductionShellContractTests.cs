using System.Diagnostics;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute() { if (OperatingSystem.IsWindows()) { Skip = "POSIX shell evidence runs on Linux/macOS hosts."; } }
}

public sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute() { if (OperatingSystem.IsWindows()) { Skip = "POSIX shell evidence runs on Linux/macOS hosts."; } }
}

[Trait("Category", "E1")]
public sealed class ProductionShellContractTests
{
    [UnixTheory]
    [InlineData(1)]
    [InlineData(42)]
    public async Task DpkgAuditNonzeroWithEmptyOutputCannotVerify(int exitCode)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("dpkg", $"exit {exitCode}");
        var result = await sandbox.RunAsync(UbuntuPackageCommandCatalog.RequireShellCommand(UbuntuPackageCommandCatalog.CreateUpgradeVerifyRequest()));
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("package_upgrade=verified", result.Output, StringComparison.Ordinal);
    }

    [UnixFact]
    public async Task CurrentAptUpdateRequestsStrictFailureSemantics()
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", "printf '0\\n'");
        sandbox.Stub("apt-get", "case \"$*\" in *APT::Update::Error-Mode=any*) exit 100;; *) exit 0;; esac");
        var result = await sandbox.RunAsync(UbuntuPackageCommandCatalog.RequireShellCommand(UbuntuPackageCommandCatalog.CreateUpdateRequest()));
        Assert.NotEqual(0, result.ExitCode);
    }

    [UnixTheory]
    [InlineData("192.0.2.1 49152 192.0.2.2 22", "22")]
    [InlineData("192.0.2.1 49152 192.0.2.2 2202", "2202")]
    [InlineData("", null)]
    [InlineData("192.0.2.1 49152 192.0.2.2 0", null)]
    [InlineData("192.0.2.1 49152 192.0.2.2 65536", null)]
    [InlineData("192.0.2.1 49152 192.0.2.2 22 injected", null)]
    public async Task ServerPortComesFromSessionEvidence(string connection, string? expected)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Environment["SSH_CONNECTION"] = connection;
        var shell = UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.SshSessionPortRead).ShellCommand;
        Assert.NotNull(shell);
        var result = await sandbox.RunAsync(shell);
        if (expected is null) { Assert.NotEqual(0, result.ExitCode); }
        else { Assert.Equal(0, result.ExitCode); Assert.Equal(expected, result.Output.Trim()); }
    }

    [UnixFact]
    public async Task UfwReadUsesPasswordlessSudoForNonRoot()
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", "printf '1000\\n'");
        sandbox.Stub("sudo", "[ \"$1\" = -n ] || exit 77; shift; export RC_ELEVATED=1; exec \"$@\"");
        sandbox.Stub("ufw", "[ \"${RC_ELEVATED-}\" = 1 ] || exit 77; printf 'Status: inactive\\n'");
        foreach (var id in new[] { RemoteCommandCatalog.UbuntuUfwStatusRead, RemoteCommandCatalog.UbuntuUfwDetectionRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwAddedRulesRead })
        {
            var result = await sandbox.RunAsync(UbuntuFactCommandCatalog.RequireKnown(id).ShellCommand!);
            Assert.Equal(0, result.ExitCode);
        }
    }

    [UnixTheory]
    [InlineData(true, 0)]
    [InlineData(false, 77)]
    public async Task UfwRootAndDeniedPrivilegeRemainDistinct(bool root, int expectedExit)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", root ? "printf '0\\n'" : "printf '1000\\n'");
        sandbox.Stub("sudo", "exit 1");
        sandbox.Stub("ufw", "printf 'Status: inactive\\n'");
        var result = await sandbox.RunAsync(UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwRuleListRead).ShellCommand!);
        Assert.Equal(expectedExit, result.ExitCode);
        var parsed = UbuntuServerFactParser.ParseUfwRuleList(new RemoteCommandResult(result.ExitCode, result.Output, string.Empty, TimeSpan.Zero));
        Assert.Equal(root ? UfwRuleListReadStatus.Complete : UfwRuleListReadStatus.PrivilegeFailure, parsed.Status);
    }

    [UnixFact]
    public async Task MissingUfwIsNotAPrivilegeOrParseFailure()
    {
        using var sandbox = new ShellSandbox();
        var result = await sandbox.RunAsync("command() { return 1; }; " + UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwRuleListRead).ShellCommand);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ufw=unavailable", result.Output.Trim());
    }

    [UnixTheory]
    [InlineData("lock")]
    [InlineData("write-failure")]
    [InlineData("stale")]
    [InlineData("symlink")]
    public async Task AuthorizedKeyFailureNeverReplacesPreviousOrConcurrentRecords(string fault)
    {
        using var sandbox = new ShellSandbox();
        var directory = Path.Combine(sandbox.Root, "account-ssh");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "authorized_keys");
        const string original = "# original without LF";
        await File.WriteAllTextAsync(file, original);
        if (fault == "lock") { Directory.CreateDirectory(Path.Combine(directory, ".vpsready-authorized-keys.lock")); }
        if (fault == "write-failure") { sandbox.Stub("mv", "exit 1"); }
        if (fault == "stale") { sandbox.Stub("cmp", "printf '# concurrent' >> authorized_keys; exit 1"); }
        if (fault == "symlink")
        {
            var other = Path.Combine(sandbox.Root, "other-records");
            File.Move(file, other);
            File.CreateSymbolicLink(file, other);
        }
        var key = CreateKey();
        var result = await sandbox.RunAsync(KeyScript(UbuntuAuthorizedKeysCommandCatalog.CreateInstallRequest(key), key, directory));
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(await File.ReadAllTextAsync(file) == original + (fault == "stale" ? "# concurrent" : ""));
    }

    [UnixTheory]
    [InlineData("restrict")]
    [InlineData("command=\"echo hello world\",no-pty")]
    public async Task ExistingRestrictedSelectedKeyIsRefusedWithoutAnUnrestrictedDuplicate(string options)
    {
        using var sandbox = new ShellSandbox();
        var directory = Path.Combine(sandbox.Root, "account-ssh");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "authorized_keys");
        var key = CreateKey();
        var original = options + " " + key.CanonicalText + " retained comment\n";
        await File.WriteAllTextAsync(file, original);
        var result = await sandbox.RunAsync(KeyScript(UbuntuAuthorizedKeysCommandCatalog.CreateInstallRequest(key), key, directory));
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(original == await File.ReadAllTextAsync(file));
    }

    [UnixTheory]
    [InlineData("")]
    [InlineData("# previous comment")]
    [InlineData("# previous comment\r\n")]
    [InlineData("ssh-rsa OTHER previous record")]
    [InlineData("ssh-rsa OTHER previous record\n")]
    public async Task AuthorizedKeysAppendPreservesEveryExistingByteAndRecord(string original)
    {
        using var sandbox = new ShellSandbox();
        var directory = Path.Combine(sandbox.Root, "account-ssh");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "authorized_keys");
        await File.WriteAllTextAsync(file, original);
        var key = CreateKey();
        var shell = KeyScript(UbuntuAuthorizedKeysCommandCatalog.CreateInstallRequest(key), key, directory);
        Assert.Equal(0, (await sandbox.RunAsync(shell)).ExitCode);
        var after = await File.ReadAllTextAsync(file);
        var separator = original.Length > 0 && !original.EndsWith('\n') ? "\n" : "";
        Assert.True(after == original + separator + key.CanonicalText + "\n", "Previous records must remain byte-preserved and separate.");
        Assert.Equal(0, (await sandbox.RunAsync(shell)).ExitCode);
        Assert.True(after == await File.ReadAllTextAsync(file), "Repeated deployment must not append another key.");
    }

    [UnixTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommentKeyTextIsNotAnActiveKey(bool wholeLineComment)
    {
        using var sandbox = new ShellSandbox();
        var directory = Path.Combine(sandbox.Root, "account-ssh");
        Directory.CreateDirectory(directory);
        var key = CreateKey();
        var original = (wholeLineComment ? "# " : "ssh-rsa OTHER comment ") + key.CanonicalText + "\n";
        await File.WriteAllTextAsync(Path.Combine(directory, "authorized_keys"), original);
        var result = await sandbox.RunAsync(KeyScript(UbuntuAuthorizedKeysCommandCatalog.CreateInspectRequest(key), key, directory));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("present=false", result.Output.Trim());
    }

    [UnixTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task RealExistingKeyRecordsWithOptionsRemainByteExact(string newline)
    {
        if (OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("This POSIX probe must be skipped on Windows."); }
        using var sandbox = new ShellSandbox();
        var directory = Path.Combine(sandbox.Root, "account-ssh");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "authorized_keys");
        var original = CreateKey(1).CanonicalText + " first" + newline
            + "command=\"echo retained\",no-pty " + CreateKey(2).CanonicalText + " second";
        await File.WriteAllTextAsync(file, original);
        var mode = File.GetUnixFileMode(file);
        var key = CreateKey();
        Assert.Equal(0, (await sandbox.RunAsync(KeyScript(UbuntuAuthorizedKeysCommandCatalog.CreateInstallRequest(key), key, directory))).ExitCode);
        Assert.True(await File.ReadAllTextAsync(file) == original + "\n" + key.CanonicalText + "\n");
        Assert.Equal(mode, File.GetUnixFileMode(file));
        var backup = Assert.Single(Directory.GetFiles(directory, ".vpsready-authorized-keys.backup.*"));
        Assert.True(await File.ReadAllTextAsync(backup) == original);
    }

    private static PreparedPublicKey CreateKey(byte seed = 0)
    {
        var text = "ssh-ed25519 " + Convert.ToBase64String(OpenSshPublicKeyUtilities.EncodePublicKey(new Ed25519PrivateKeyParameters(Enumerable.Repeat(seed, 32).ToArray(), 0).GeneratePublicKey()));
        using var material = new PublicKeyDeploymentMaterial(text);
        Assert.True(UbuntuAuthorizedKeysCommandCatalog.TryPrepare(material, out var key));
        return key!;
    }

    private static string KeyScript(RemoteCommand command, PreparedPublicKey key, string directory) => UbuntuAuthorizedKeysCommandCatalog.RequireShellCommand(command, key.CanonicalText)
        .Replace("\"${HOME:?}/.ssh\"", RemoteCommandArguments.QuotePosixArgument(directory), StringComparison.Ordinal);
}

internal sealed class ShellSandbox : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("vpsready-shell-").FullName;
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    public ShellSandbox()
    {
        Directory.CreateDirectory(Path.Combine(Root, "bin"));
        if (OperatingSystem.IsMacOS())
        {
            Stub("stat", "if [ \"$1\" = -c ]; then case \"$2\" in %u) fmt=%u;; %g) fmt=%g;; %a) fmt=%Lp;; *) exit 2;; esac; shift 2; exec /usr/bin/stat -f \"$fmt\" \"$@\"; fi; exit 2");
        }
    }

    public void Stub(string name, string script)
    {
        var path = Path.Combine(Root, "bin", name);
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n", new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    public async Task<ShellResult> RunAsync(string script)
    {
        var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = Root };
        start.Environment["PATH"] = Path.Combine(Root, "bin") + ":/usr/bin:/bin:/usr/sbin:/sbin";
        foreach (var pair in Environment) { start.Environment[pair.Key] = pair.Value; }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed record ShellResult(int ExitCode, string Output, string Error)
{
    public override string ToString() => $"ShellResult [exit={ExitCode}]";
}
