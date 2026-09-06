# Contributing to VPSReady

[Project home](README.md) · [Documentation](docs/README.md) · [Project status](docs/PROJECT_STATUS.md)

VPSReady changes access-sensitive server configuration. Keep contributions
small, reviewable and explicit about what was verified.

## Before starting

1. Read [AGENTS.md](AGENTS.md) and its required documents in order.
2. Search existing issues. Link every non-trivial change to one scoped issue
   before implementation, with acceptance criteria and a primary owner.
3. Maintain one persistent `## Codex Workpad` comment on the issue; update it
   at meaningful checkpoints, verification, handoff and before ending a run.
4. Check the [approved scope](docs/V0.1_CORE_BASIC_SPEC.md). New modules,
   dependency-policy changes or privacy changes need the appropriate review;
   they are not implicit parts of a repair.

Agents must not request real-VPS credentials or use a real VPS for development.
Use deterministic fakes, fault injection and approved contained fixtures.

## Choose the correct branch

| Change | Base | Working branch |
| --- | --- | --- |
| Routine feature or fix | `development` | `feature/<issue>-<slug>` or `fix/<issue>-<slug>` |
| Authorized candidate repair or documentation follow-up | The specified `release/*` branch | An issue-scoped `fix/<issue>-<slug>` branch |
| Stable promotion | Explicit Owner authorization required | Follow the approved release procedure. |

Use an isolated worktree for each issue. Do not let two developers edit the
same workspace concurrently, discard others' changes, force-push shared
history or push product changes to `main` without explicit Owner approval.

## Build and verify locally

Use the SDK selected by [global.json](global.json). From the repository root:

```bash
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj --configuration Release --no-build
dotnet format VpsReady.slnx --verify-no-changes --no-restore
bash eng/verify-tracked-secrets.sh
bash eng/verify-user-troubleshooting-guide.sh
git diff --check
```

On Windows, the Bash checks need a compatible Bash environment, such as Git
Bash or WSL. The complete [quality-check baseline](docs/development/QUALITY_CHECKS.md)
also includes analyzers and dependency/vulnerability inventory. Do not suppress
warnings or regenerate lock files merely to make a failing check green.

The repaired release checkout additionally provides
`bash eng/verify-gitignore.sh`. Run it there; the older development baseline
does not contain this guard. Missing guard evidence is NOT RUN, never PASS.

Contained protocol checks have additional fixture requirements. Read their
setup scripts and the [test strategy](docs/testing/TEST_STRATEGY.md) before
running them. Do not run remote UFW or package-management commands against
your personal host as a substitute for an isolated Ubuntu fixture.

## Report evidence accurately

| Class | Evidence | Does not establish |
| --- | --- | --- |
| E0 | Static checks, build, analyzers and repository checks | Remote behavior |
| E1 | Unit and contract tests | Real SSH or infrastructure |
| E2 | Stateful simulation and fault injection | Actual host behavior |
| E3 | Contained/local protocol checks | A public or production VPS |
| E4 | Packaging and matching-host startup checks | Real-server correctness |
| E5 | Owner-produced real-VPS evidence | Other untested candidates or environments |

Record the exact SHA, commands, outcomes and skip/not-run reasons. A green
compile is not Done. Documentation-only checks are E0 documentation evidence;
do not attach an earlier revision's runtime results to a new documentation SHA.
Until Owner evidence exists, state **REAL VPS: NOT TESTED**.

## Pull-request checklist

- [ ] Link the issue and state the correct base and head revision.
- [ ] Explain the user-visible change and map it to acceptance criteria.
- [ ] Record relevant checks, failures, skips and evidence boundaries.
- [ ] Keep remote commands out of views and preserve access-safety invariants.
- [ ] Redact before persistence, UI display, export or public issue text.
- [ ] Exclude generated output, credentials and private keys from Git.
- [ ] Update affected documentation and the issue Workpad.
- [ ] Label self-review as self-review; do not claim independent QA.

## Report problems safely

Follow the [safe troubleshooting path](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md#safe-troubleshooting-path).
Review sanitized reports before sharing them. Do not post server addresses,
credentials, private keys, raw terminal transcripts or sensitive exploit
details in a public issue. For sensitive security reports, arrange a private
channel with the maintainer before sending confidential details.

## Keep documentation synchronized

Shared root Markdown and `docs/` should stay aligned between `development`
and the active `release/*` branch. A documentation change is not complete after
updating only one branch.

1. Link both branch updates to the same issue and preserve newer facts from
   either branch; do not blindly overwrite the development ExecPlan.
2. Apply only the reviewed documentation changes on an issue branch from each
   target. Do not merge an entire release branch merely to copy its README.
3. Where implementation differs, keep an explicit dated branch/revision note.
   Candidate behavior, script availability and test results must name their
   actual source; identical docs do not imply identical binaries.
4. Check links, guide semantics and documentation rendering on both branches,
   verify that no non-documentation files changed, and record both PRs/SHAs.
5. After fetching the remote refs, verify shared documentation parity:

```bash
git diff --exit-code origin/development origin/release/0.1.0 -- '*.md' docs/
```

Empty output and exit code 0 mean the checked documentation matches. This is
E0 documentation evidence only, not a runtime, CI or release-readiness gate.

## Documentation conventions

- Use one clear page title, sentence-case headings and repository-relative links.
- Lead with the user's task; link to authoritative contracts instead of copying them.
- Separate supported scope, implemented behavior and measured evidence.
- Date status snapshots and tie results to exact source and artifact revisions.
- Keep the Thai introduction aligned with the English overview; technical
  contracts remain in the linked authoritative documents.
- Do not add a passing badge, screenshot or download claim without supporting evidence.

Contributions retain the project's [Apache License 2.0](LICENSE). This guide
does not change the license or any Owner approval boundary.
