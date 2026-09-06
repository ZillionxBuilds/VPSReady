using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PackageNoninteractiveContractTests
{
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
