---
name: verify-change
description: Build, test, and demonstrate the effect of the code changes you just made, then print a fully transparent summary of exactly what ran and how the change is reflected. Use when you want to test, verify, check, try, or smoke-test an in-progress change before committing or opening a PR, and want to see "what is going on" step by step. Works for the .NET CLI, the MockToolingServer, and the Python autoTriage tool. Never mutates Azure/Graph/git state.
allowed-tools: Bash(git:*), Bash(dotnet:*), Bash(pytest:*), Bash(python:*), Bash(pip:*), Bash(cd:*), Read
---

# Verify Change Skill

Take whatever you just changed in the working tree, figure out what it affects, build it, run the
narrowest relevant tests, optionally show the change in action, and report back a transparent,
step-by-step summary. The goal is: **you change something, you run this, and you see exactly how it
is reflected** — with no hidden steps.

## Usage

```
/verify-change            # Auto-detect changes, build + test only what's affected, summarize
/verify-change --full     # Ignore scoping: mirror CI (restore + build + full test suite + pack)
/verify-change --run      # Also run the affected CLI command with --dry-run/--help to show behavior
/verify-change --branch   # Scope to the whole branch diff vs origin/main, not just the working tree
```

Flags combine, e.g. `/verify-change --run --full`.

## Transparency contract (the whole point of this skill)

Every run MUST obey these rules. This is what "everything should be transparent" means:

1. **Echo before you execute.** Print the exact command in a fenced block *before* running it, so
   the reader can see and reproduce it.
2. **Show the real result.** Report actual pass/fail counts, warning counts, and the first lines of
   any failure. Never summarize a failure as a success. Never hide or truncate an error.
3. **Explain the "why" of each step.** State which changed file triggered which build/test — e.g.
   "ran `~PublishCommand` because `Commands/PublishCommand.cs` changed."
4. **Only safe, local, reversible actions.** Read-only git, build, test, and `--dry-run`/`--help`
   runs only. See the Safety section — this is non-negotiable.
5. **Declare the gaps.** Always end with what was NOT covered (unrun tests, skipped integration,
   manual Azure paths). A green result with hidden gaps is a transparency failure.

## What this skill does (overview)

```
Step 1  Detect changes        git status/diff (read-only)
Step 2  Classify surface      map changed files -> subsystem(s)
Step 3  Build affected        dotnet build (warnings are errors) / no build for docs-only
Step 4  Test narrowest        dotnet test --filter <derived>  /  pytest <affected>
Step 5  Demonstrate (--run)   dotnet run -- <cmd> --dry-run | --help   (safe only)
Step 6  Summarize             the transparent report (template below)
```

---

## Step 1 — Detect changes

Run these read-only commands and print the file list you found. Default scope is the working tree
(staged + unstaged) vs `HEAD`. With `--branch`, also include the full branch diff.

```bash
git status --short
git diff --stat HEAD                 # staged + unstaged vs last commit (default scope)
```

With `--branch`, additionally:

```bash
git diff --stat $(git merge-base HEAD origin/main)...HEAD
```

If there are **no** changes in scope, stop and say so plainly (nothing to verify), and suggest
`--branch` in case the change is already committed on the branch.

## Step 2 — Classify the change surface

Map each changed file to a subsystem. Print the mapping so the reader sees your reasoning.

| Changed path pattern | Subsystem | Consequence |
|---|---|---|
| `src/Microsoft.Agents.A365.DevTools.Cli/**` | CLI (product) | build + test CLI |
| `src/Microsoft.Agents.A365.DevTools.MockToolingServer/**` | Mock server | build + (fidelity) test |
| `src/Tests/**` | Tests | build + run the touched test classes |
| `src/Directory.*.props`, `**/*.csproj`, `src/dirs.proj`, `src/tests.proj`, `src/global.json` | Build/deps | treat as broad: build everything, run full suite |
| `autoTriage/**` | Python tool | pytest (see Step 4b) |
| `docs/**`, `**/*.md`, `.github/**` (non-workflow) | Docs/meta | no build; report only |

If the change spans multiple subsystems, handle each. If it touches build/dependency files, escalate
to `--full` behavior automatically and say why.

## Step 3 — Build the affected projects

All .NET commands run from `src/`. **`TreatWarningsAsErrors` is true in this repo, so the build is a
real gate — a new warning fails it.** Call that out explicitly if it happens.

For a CLI/test/mock change (building `tests.proj` compiles the product and tests together):

```bash
cd src
dotnet build tests.proj -c Release
```

For a docs-only change, skip the build and note "no build required (docs/markdown only)."

Report the result as: `SUCCESS (0 warnings)`, `SUCCESS (N warnings)`, or `FAILED` with the first
error/warning line quoted verbatim.

## Step 4a — Test the narrowest relevant scope (.NET)

Derive a `--filter` from the changed files instead of always running everything. The rule: take the
type name from each changed `*.cs` file and filter on it (this matches both the class and its test
class by fully-qualified-name substring).

Examples (state which one you used and why):

```bash
# Changed Commands/PublishCommand.cs  -> test PublishCommand + PublishCommandTests
dotnet test tests.proj -c Release --filter "FullyQualifiedName~PublishCommand"

# Changed Services/ConfigService.cs   -> test ConfigService*
dotnet test tests.proj -c Release --filter "FullyQualifiedName~ConfigService"

# Changed a test file directly        -> run that test class
dotnet test tests.proj -c Release --filter "FullyQualifiedName~SetupHelpersDisplaySetupSummaryTests"
```

If two or more areas changed, OR the mapping to a filter is uncertain, OR a build/dependency file
changed, run the full suite the way CI does (hang detection matters — a test that hangs is a failure
you want surfaced locally):

```bash
dotnet test tests.proj -c Release --blame-hang --blame-hang-timeout 5min
```

Report: `<passed> passed, <failed> failed, <skipped> skipped (<duration>)`. If anything failed, list
the failing test names and the first line of each failure message. Do not proceed as if it passed.

## Step 4b — Test the affected scope (Python autoTriage)

Only when files under `autoTriage/**` changed. Run from `autoTriage/`:

```bash
cd autoTriage
pip install -r requirements.txt      # first run only, if imports fail
pytest tests/services/test_<affected>.py     # narrow to the touched module when possible
pytest                                        # or the whole suite if the change is broad
```

Follow the autoTriage instructions file for conventions:
`.github/instructions/autotriage.instructions.md`.

## Step 5 — Demonstrate the behavior (only with `--run`)

This is the "see how it's reflected" part. Run the affected CLI command **from source** so it uses
the freshly built code, and show its output. Use only safe, non-mutating invocations:

```bash
# Help / shape of a command whose options changed:
dotnet run --project src/Microsoft.Agents.A365.DevTools.Cli -- <command> --help

# Behavior of a command that supports it, without touching real resources:
dotnet run --project src/Microsoft.Agents.A365.DevTools.Cli -- publish --dry-run
dotnet run --project src/Microsoft.Agents.A365.DevTools.Cli -- cleanup --dry-run
dotnet run --project src/Microsoft.Agents.A365.DevTools.Cli -- config display
```

Quote the key output lines and explain, in one or two sentences, how they reflect the change you
made (e.g. "the new `--messaging-endpoint` option now appears in `publish --help` and is rejected
when passed without `--m365`, matching the intended precondition").

> On Windows/PowerShell the `dotnet` commands are identical; only script invocation differs
> (`./scripts/cli/install-cli.ps1` instead of the `.sh`). Adapt paths to the current OS.

## Step 6 — Print the transparent summary

Always finish with this exact structure. Keep commands and real numbers in it.

```
# Verify Change Summary

## 1. What changed
- <N> file(s) across <subsystem list>
  - [CLI]   src/.../PublishCommand.cs
  - [Tests] src/Tests/.../PublishCommandTests.cs

## 2. Build
Command: dotnet build tests.proj -c Release   (from src/)
Result:  SUCCESS (0 warnings)   |   FAILED: <first error line>

## 3. Tests
Command: dotnet test tests.proj -c Release --filter "FullyQualifiedName~PublishCommand"
Reason:  PublishCommand.cs changed
Result:  24 passed, 0 failed, 0 skipped (3.2s)
Failures (if any):
  - PublishCommandTests.DryRun_DoesNotWriteZip  ->  Expected no file, found manifest.zip

## 4. Behavior demonstrated   (only if --run)
Command: dotnet run --project src/...Cli -- publish --help
Observed: <key lines>
Reflects the change because: <one or two sentences>

## 5. Not covered
- Full suite not run (scoped filter only)
- Integration tests under Tests/Integration skipped
- No real Azure/Graph path exercised (dry-run/help only)

## Verdict
PASS  - build clean, targeted tests green; change behaves as intended.
   |  FAIL - <what broke and the single most likely cause>
```

---

## Safety (hard limits)

This skill is for testing your own change locally. It must never:

- Run mutating CLI commands against real services. **Forbidden without a dry-run/read-only mode:**
  `setup all`, `setup blueprint`, `setup permissions`, `create-instance`, `cleanup` (without
  `--dry-run`), `develop-mcp publish` / `unpublish` / `register-external-mcp-server`,
  `publish` (without `--dry-run`). Prefer `--help`, `--dry-run`, `config display`, and
  `query-entra`/`develop-mcp list-*` read-only calls.
- Change git state. No `commit`, `push`, `reset`, `restore`, `checkout`, `clean`, or `stash`.
  Git is used read-only (`status`, `diff`, `merge-base`).
- Modify source, config, or dependency files. This skill reads and runs; it does not edit.
- Post to GitHub, Teams, or any network endpoint.

If verifying the change genuinely requires a mutating action, stop and tell the user exactly which
command to run themselves, and why — do not run it for them.

## Notes

- Default scope is the working tree (staged + unstaged). Use `--branch` for the full branch diff vs
  `origin/main` (the view a reviewer/CI sees).
- `--full` mirrors CI: `dotnet restore dirs.proj && dotnet restore tests.proj`, build both, then
  `dotnet test tests.proj -c Release --blame-hang --blame-hang-timeout 5min`, then
  `dotnet pack dirs.proj -c Release`. Use it before pushing when the change is broad.
- This skill only reports; fixing failures is a separate step. If tests fail, surface them clearly
  and let the user decide next actions.

## See Also
- `/review-staged` — code-quality review of the same changes (complementary: this skill *runs* the
  change, review-staged *reads* it).
- `src/DEVELOPER.md`, `docs/design.md`, and `.github/copilot-instructions.md` — build/test/standards.
