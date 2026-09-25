using System.Text.RegularExpressions;

namespace VpsReady.UnitTests;

// These execute the checked-in CI scripts in disposable local Git repositories.
// They do not emulate GitHub scheduling, enforcement, or hosted runner evidence.
[Trait("Category", "E0")]
public sealed class MainCiGateTests
{
    private static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "VpsReady.slnx"))) { return directory.FullName; }
            }
            throw new InvalidOperationException("Repository fixture source is unavailable.");
        }
    }
    private static string Workflow => File.ReadAllText(Path.Combine(Root, ".github/workflows/blind-ci.yml"));

    [Theory]
    [InlineData("pull_request", "main", true)]
    [InlineData("pull_request", "development", true)]
    [InlineData("pull_request", "release/0.1.0", true)]
    [InlineData("pull_request", "feature/example", false)]
    [InlineData("push", "main", true)]
    [InlineData("push", "development", true)]
    [InlineData("push", "release/0.1.0", true)]
    [InlineData("push", "feature/example", false)]
    public void BranchSelectionUsesTargetBranch(string eventName, string target, bool expected)
    {
        var section = Workflow.Split($"  {eventName}:\n", StringSplitOptions.None)[1].Split("\n  ", StringSplitOptions.None)
            .TakeWhile(line => line.Length == 0 || char.IsWhiteSpace(line[0])).ToArray();
        var branches = Regex.Matches(string.Join('\n', section), "- (?:\"([^\"]+)\"|([^\\s]+))")
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        Assert.Equal(expected, branches.Any(pattern => pattern == target || (pattern == "release/**" && target.StartsWith("release/", StringComparison.Ordinal))));
        Assert.DoesNotContain("pull_request_target:", Workflow, StringComparison.Ordinal);
        Assert.Contains("permissions:\n  contents: read\n", Workflow, StringComparison.Ordinal);
    }

    public static TheoryData<int, string> ChildResults
    {
        get
        {
            var data = new TheoryData<int, string> { { -1, "success" } };
            for (var child = 0; child < 5; child++) { foreach (var result in new[] { "failure", "skipped", "cancelled" }) { data.Add(child, result); } }
            return data;
        }
    }

    [UnixTheory]
    [MemberData(nameof(ChildResults))]
    public async Task RequiredExecutesActualAggregateAndFailsClosed(int changedChild, string result)
    {
        var required = Workflow.Split("\n  required:\n", StringSplitOptions.None)[1];
        Assert.Contains("    name: required\n    if: always()", required, StringComparison.Ordinal);
        using var sandbox = new ShellSandbox();
        string[] variables = ["RESOLVE_RESULT", "VALIDATE_RESULT", "LOCAL_PROTOCOL_RESULT", "PACKAGE_RESULT", "PROVENANCE_RESULT"];
        for (var index = 0; index < variables.Length; index++) { sandbox.Environment[variables[index]] = index == changedChild ? result : "success"; }
        var script = required.Split("        run: |\n", StringSplitOptions.None)[1];
        var actual = await sandbox.RunAsync("bash <<'R18_AGGREGATE'\n" + script + "\nR18_AGGREGATE\n");
        Assert.Equal(changedChild == -1, actual.ExitCode == 0);
    }

    [UnixFact]
    public async Task FirstPromotionBootstrapsFromWorkflowAndRevalidatesMovedBase()
    {
        using var sandbox = new ShellSandbox();
        var action = File.ReadAllText(Path.Combine(Root, ".github/actions/prepare-prospective-validation/action.yml"));
        var script = string.Join('\n', action.Split("      run: |\n", StringSplitOptions.None)[1].Split('\n').Select(line => line.Length >= 8 ? line[8..] : line));
        File.WriteAllText(Path.Combine(sandbox.Root, "prepare.sh"), script);
        var origin = Path.Combine(sandbox.Root, "origin"); Directory.CreateDirectory(origin);
        var prepared = await sandbox.RunAsync("git init -b main origin && cd origin && git -c user.name=Fixture -c user.email=fixture@invalid.local -c commit.gpgsign=false commit --allow-empty -m base");
        Assert.True(prepared.ExitCode == 0, prepared.Error);
        var baseResult = await sandbox.RunAsync("git -C origin rev-parse HEAD");
        var baseSha = baseResult.Output.Trim();
        var actionPath = Path.Combine(origin, ".github/actions/prepare-prospective-validation"); Directory.CreateDirectory(actionPath);
        File.WriteAllText(Path.Combine(actionPath, "action.yml"), action);
        File.Copy(Path.Combine(Root, "VpsReady.slnx"), Path.Combine(origin, "VpsReady.slnx"));
        var headResult = await sandbox.RunAsync("cd origin && git checkout -b repair && git add . && git -c user.name=Fixture -c user.email=fixture@invalid.local -c commit.gpgsign=false commit -m product && git rev-parse HEAD");
        Assert.True(headResult.ExitCode == 0, headResult.Error);
        var headSha = headResult.Output.Trim().Split('\n')[^1];
        Assert.Equal(4, Regex.Count(Workflow, Regex.Escape("ref: ${{ github.workflow_sha }}")));
        var oldTree = await sandbox.RunAsync($"git -C origin ls-tree -r {baseSha}"); Assert.Empty(oldTree.Output);
        var clone = await sandbox.RunAsync("git clone origin job"); Assert.True(clone.ExitCode == 0, clone.Error);
        Assert.True(File.Exists(Path.Combine(sandbox.Root, "job/.github/actions/prepare-prospective-validation/action.yml")));
        sandbox.Environment["EVENT_NAME"] = "pull_request";
        sandbox.Environment["SOURCE_SHA"] = headSha;
        sandbox.Environment["PR_HEAD_SHA"] = headSha;
        sandbox.Environment["PR_BASE_SHA"] = baseSha;
        sandbox.Environment["PR_BASE_REF"] = "main";
        sandbox.Environment["PUSH_SHA"] = string.Empty;
        sandbox.Environment["PROVIDED_VALIDATION_BASE_SHA"] = string.Empty;
        sandbox.Environment["EXPECTED_VALIDATION_SHA"] = string.Empty;
        sandbox.Environment["GITHUB_ENV"] = Path.Combine(sandbox.Root, "env");
        sandbox.Environment["GITHUB_OUTPUT"] = Path.Combine(sandbox.Root, "outputs");
        var first = await sandbox.RunAsync("cd job && bash ../prepare.sh"); Assert.True(first.ExitCode == 0, first.Error);
        var firstSha = (await sandbox.RunAsync("git -C job rev-parse HEAD")).Output.Trim();
        var parents = (await sandbox.RunAsync("git -C job show -s --format=%P HEAD")).Output.Trim();
        Assert.Equal($"{baseSha} {headSha}", parents);
        Assert.True(File.Exists(Path.Combine(sandbox.Root, "job/VpsReady.slnx")));
        var move = await sandbox.RunAsync("cd origin && git checkout main && git -c user.name=Fixture -c user.email=fixture@invalid.local -c commit.gpgsign=false commit --allow-empty -m base-moved"); Assert.True(move.ExitCode == 0, move.Error);
        // Matrix workers reproduce the resolver's exact pair, even after a base move.
        sandbox.Environment["PROVIDED_VALIDATION_BASE_SHA"] = baseSha;
        sandbox.Environment["EXPECTED_VALIDATION_SHA"] = firstSha;
        var reproduce = await sandbox.RunAsync("cd job && bash ../prepare.sh"); Assert.True(reproduce.ExitCode == 0, reproduce.Error);
        // A new resolution must use the new base and produce new evidence.
        sandbox.Environment["PROVIDED_VALIDATION_BASE_SHA"] = string.Empty;
        sandbox.Environment["EXPECTED_VALIDATION_SHA"] = string.Empty;
        var moved = await sandbox.RunAsync("cd job && bash ../prepare.sh"); Assert.True(moved.ExitCode == 0, moved.Error);
        var movedSha = (await sandbox.RunAsync("git -C job rev-parse HEAD")).Output.Trim(); Assert.NotEqual(firstSha, movedSha);
        var provenance = File.ReadAllText(Path.Combine(sandbox.Root, "job/TestResults/ci/sha-provenance.env"));
        Assert.Contains($"PR_HEAD_SHA={headSha}", provenance, StringComparison.Ordinal);
        Assert.Contains($"PR_BASE_SHA={baseSha}", provenance, StringComparison.Ordinal);
        var newBase = (await sandbox.RunAsync("git -C origin rev-parse main")).Output.Trim();
        Assert.Contains($"VALIDATION_BASE_SHA={newBase}", provenance, StringComparison.Ordinal);
        sandbox.Environment["EXPECTED_VALIDATION_SHA"] = firstSha;
        var reject = await sandbox.RunAsync("cd job && bash ../prepare.sh");
        Assert.NotEqual(0, reject.ExitCode);
        Assert.Contains("identity mismatch", reject.Error, StringComparison.Ordinal);
    }
}
