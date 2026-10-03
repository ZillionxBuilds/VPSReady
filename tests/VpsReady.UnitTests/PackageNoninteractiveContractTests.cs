using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PackageNoninteractiveContractTests
{
    [UnixTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewUsesApplyPrivilegeEnvironmentAndHashesVersionSelection(bool root)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", root ? "printf '0\\n'" : "printf '1000\\n'");
        sandbox.Stub("sudo", "[ \"$1\" = -n ] || exit 77; shift; exec env -i PATH=\"$PATH\" \"$@\"");
        if (OperatingSystem.IsMacOS()) { sandbox.Stub("sha256sum", "exec /usr/bin/shasum -a 256"); }
        sandbox.Environment["APT_CONFIG"] = "ignored-fixture";
        var selection = Path.Combine(sandbox.Root, "selection");
        sandbox.Stub("apt-get", """
            [ "${DEBIAN_FRONTEND-}" = noninteractive ] && [ "${LC_ALL-}" = C ] && [ "${LANG-}" = C ] || exit 31
            [ -z "${APT_CONFIG-}" ] || exit 32
            case "$*" in *--simulate*--assume-yes*Dpkg::Options::=--force-confold*upgrade*) ;; *) exit 33;; esac
            if read -r answer; then exit 34; fi
            """ + "\ncat " + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(selection));
        var command = UbuntuPackageCommandCatalog.CreateUpgradePlanRequest();
        var shell = UbuntuPackageCommandCatalog.RequireShellCommand(command).Replace("PATH=/usr/sbin:/usr/bin:/sbin:/bin",
            "PATH=" + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(Path.Combine(sandbox.Root, "bin") + ":/usr/bin:/bin"), StringComparison.Ordinal);
        await File.WriteAllTextAsync(selection, "Reading package lists...\nInst fixture [1.0] (2.0 Ubuntu:24.04 [amd64])\nConf fixture (2.0 Ubuntu:24.04 [amd64])\n");
        var first = await sandbox.RunAsync(shell);
        await File.WriteAllTextAsync(selection, "Reading package lists...\nInst fixture [1.0] (3.0 Ubuntu:24.04 [amd64])\nConf fixture (3.0 Ubuntu:24.04 [amd64])\n");
        var second = await sandbox.RunAsync(shell);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        var firstEvidence = VpsReady.Core.Remote.CommandParserEvidence.Parse(command.Id.Value, first.Output);
        var secondEvidence = VpsReady.Core.Remote.CommandParserEvidence.Parse(command.Id.Value, second.Output);
        Assert.NotNull(firstEvidence);
        Assert.NotNull(secondEvidence);
        Assert.Equal(1, firstEvidence.Number);
        Assert.Equal(firstEvidence.Number, secondEvidence.Number);
        Assert.NotEqual(firstEvidence.Fingerprint, secondEvidence.Fingerprint);
        Assert.DoesNotContain("fixture", first.Output, StringComparison.Ordinal);
    }
    [UnixTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpgradeReceivesExplicitEnvironmentKeepOldConffilesAndClosedStdin(bool root)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", root ? "printf '0\\n'" : "printf '1000\\n'");
        sandbox.Environment["DEBIAN_FRONTEND"] = "wrong";
        sandbox.Environment["LC_ALL"] = "C";
        sandbox.Environment["APT_CONFIG"] = "untrusted-fixture";
        var conffile = Path.Combine(sandbox.Root, "user-config");
        await File.WriteAllTextAsync(conffile, "user-maintained-configuration\n");
        sandbox.Stub("sudo", "[ \"$1\" = -n ] || exit 77; shift; exec env -i PATH=\"$PATH\" \"$@\"");
        sandbox.Stub("apt-get", "conffile=" + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(conffile) + "\n" + """
            [ "${DEBIAN_FRONTEND-}" = noninteractive ] || exit 31
            [ "${LC_ALL-}" = C ] && [ "${LANG-}" = C ] || exit 32
            [ -z "${APT_CONFIG-}" ] || exit 36
            case "$*" in *Dpkg::Options::=--force-confold*) ;; *) exit 33;; esac
            case "$*" in *force-confnew*|*force-all*|*force-confdef*) exit 34;; esac
            if read -r answer; then exit 35; fi
            # A conflicting vendor revision must be retained separately by the
            # fixture's keep-old decision, never replace the administrator file.
            printf 'new-vendor-configuration\n' > "$conffile.dpkg-dist"
            printf 'fixture_keep_old=true\n'
            """);
        var shell = UbuntuPackageCommandCatalog.RequireShellCommand(UbuntuPackageCommandCatalog.CreateUpgradeApplyRequest())
            .Replace("PATH=/usr/sbin:/usr/bin:/sbin:/bin", "PATH=" + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(Path.Combine(sandbox.Root, "bin") + ":/usr/bin:/bin"), StringComparison.Ordinal);
        // The caller shell has additional stdin data. Upgrade must not consume it.
        var result = await sandbox.RunAsync("sh -c " + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(shell) + " <<'FIXTURE'\nunexpected-input\nFIXTURE\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("fixture_keep_old=true", result.Output.Trim());
        Assert.Equal("user-maintained-configuration\n", await File.ReadAllTextAsync(conffile));
        Assert.Equal("new-vendor-configuration\n", await File.ReadAllTextAsync(conffile + ".dpkg-dist"));
    }

    [UnixTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsupportedMaintainerInteractionIsFiniteAndRetainsFailure(bool root)
    {
        using var sandbox = new ShellSandbox();
        sandbox.Stub("id", root ? "printf '0\\n'" : "printf '1000\\n'");
        sandbox.Stub("sudo", "[ \"$1\" = -n ] || exit 77; shift; exec env -i PATH=\"$PATH\" \"$@\"");
        sandbox.Stub("apt-get", "if read -r response; then exit 35; fi; exit 42");
        var shell = UbuntuPackageCommandCatalog.RequireShellCommand(UbuntuPackageCommandCatalog.CreateUpgradeApplyRequest())
            .Replace("PATH=/usr/sbin:/usr/bin:/sbin:/bin", "PATH=" + VpsReady.Core.Remote.RemoteCommandArguments.QuotePosixArgument(Path.Combine(sandbox.Root, "bin") + ":/usr/bin:/bin"), StringComparison.Ordinal);
        Assert.Equal(42, (await sandbox.RunAsync(shell)).ExitCode);
    }
}
