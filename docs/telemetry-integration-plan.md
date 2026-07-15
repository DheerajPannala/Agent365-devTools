# Telemetry Integration Plan (Aria / 1DS)

Status: Draft / proposal
Scope: `a365` CLI (`src/Microsoft.Agents.A365.DevTools.Cli`)

## 1. Goal

Add opt-out product telemetry to the `a365` CLI so we can understand command
usage, success/failure rates, and version adoption — without collecting PII/EUII,
without breaking the public build, and without regressing startup time or
cross-platform support.

The Aria portal onboarding steps we were given target **Azure Cloud Services
(classic) Worker Roles** and the **legacy Aria SDK from an internal feed**. This
repo is a public, cross-platform `dotnet tool`. This document adapts the intent
of those steps to how this repository is actually built.

## 2. Why the portal instructions do not apply as-is

| Portal step | Assumes | Reality in this repo |
| --- | --- | --- |
| `ServiceConfiguration.*.cscfg`, `ServiceDefinition.csdef` | Cloud Services (classic) | No such files; nothing to configure |
| `WorkerRole` + `OnStart/RunAsync/OnStop` | Long-running role | Entry point is `Main()` in [Program.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Program.cs), `System.CommandLine` handlers |
| `RoleEnvironment.GetConfigurationSettingValue(...)` | ServiceRuntime API | Config via `ConfigService`, env vars, `a365.config.json` |
| Add `msasg.pkgs.visualstudio.com/.../ARIA-SDK` feed, deselect others | Internal-only build | [src/nuget.config](../src/nuget.config) clears sources and uses only nuget.org so external contributors + CI can restore |
| `Microsoft.Applications.Telemetry.Azure` | Legacy Aria package | Not on nuget.org (404). Superseded by 1DS `Microsoft.Applications.Events` |
| Hardcode app token in committed `.cscfg` | Private repo | Public repo — token must never be committed |
| `FlushAndTearDown()` in `OnStop()` | Process runs for a long time | CLI exits in milliseconds; events dropped unless flushed synchronously on exit |

Only the event-authoring API shape (`LogManager` / `EventProperties` /
`LogEvent` / flush) carries over, and only if we move to the 1DS package.

## 3. Blocking decisions (resolve before coding)

- [ ] **B1 — SDK package + feed.** Confirm a 1DS .NET package
  (`Microsoft.Applications.Events`) that is reachable from a **public** build.
  - Preferred: a version available on nuget.org so `dotnet restore` keeps
    working for external contributors and CI.
  - If only the internal feed has it: we must NOT add that feed to the shared
    [src/nuget.config](../src/nuget.config) (breaks public restore). Options are
    (a) a build-time-only feed injected in CI, with a public fallback, or
    (b) do not ship telemetry in the public package. This needs a decision.
- [ ] **B2 — Ingestion token source.** The app/ingestion token must not be
  committed. Choose one:
  - Build-time injection (recommended): CI passes the token as an MSBuild
    property during `dotnet pack`, generating an internal constant. Empty in
    local/dev builds so telemetry is a no-op there.
  - Runtime env var (e.g. `A365_TELEMETRY_TOKEN`) for dev/testing overrides.
- [ ] **B3 — Compliance sign-off.** Confirm the event schema, retention, and
  opt-out approach meet Microsoft privacy requirements for a publicly
  distributed CLI (client telemetry from end-user machines).

## 4. Target design

A thin, injectable service behind an interface, matching the existing
`INoticeService` / `IVersionCheckService` pattern. The SDK is never referenced
outside the implementation, so the rest of the CLI stays decoupled and testable.

```
ITelemetryService
  Initialize()            // idempotent; no-op when disabled
  TrackCommand(name, exitCode, durationMs)
  Flush(timeout)          // synchronous, bounded, called on every exit path
```

- `TelemetryService` — real implementation wrapping the 1DS `LogManager`.
- `NoOpTelemetryService` — default when telemetry is disabled (opt-out, CI, no
  token, or non-public build). Guarantees zero network / zero cost.
- A single factory decides which to register at startup.

Telemetry is **disabled** when any of the following is true:
- `A365_TELEMETRY_OPTOUT` is set to `1`/`true` (mirrors `DOTNET_CLI_TELEMETRY_OPTOUT`).
- `VersionCheckHelper.IsRunningInCiCd()` returns true.
- No ingestion token is compiled in (dev/local builds).

## 5. Implementation phases

### Phase 1 — Dependency and configuration plumbing
- [ ] Add the chosen 1DS package version to [src/Directory.Packages.props](../src/Directory.Packages.props)
      (central package management; do not put a version in the `.csproj`).
- [ ] Add a versionless `<PackageReference>` to
      [the CLI csproj](../src/Microsoft.Agents.A365.DevTools.Cli/Microsoft.Agents.A365.DevTools.Cli.csproj).
- [ ] Add `TelemetryConstants.cs` under
      [Constants/](../src/Microsoft.Agents.A365.DevTools.Cli/Constants) for the
      env var names, event names, and property keys (no hardcoded token).
- [ ] Implement the token source per B2 (MSBuild-generated constant and/or env var).

### Phase 2 — Service
- [ ] `Services/ITelemetryService.cs`, `Services/TelemetryService.cs`,
      `Services/NoOpTelemetryService.cs`.
- [ ] Set **client** semantic context (app version, session id, OS, CI flag).
      Do NOT call `SetCloudServiceInformation` — this is not a cloud service.
- [ ] Register in `ConfigureServices` in [Program.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Program.cs)
      alongside `INoticeService` / `IVersionCheckService`, using the enablement
      factory from section 4.

### Phase 3 — Wire into the command lifecycle
- [ ] Initialize telemetry after the service provider is built (cheap; no send).
- [ ] Record command name, exit code, and duration around
      `parser.InvokeAsync(args)`.
- [ ] Flush with a short timeout in the existing `finally` block in `Main`
      (next to `loggerFactory.Dispose()`), so events are not lost on the
      short-lived process. Every exit path (success, handled error, unhandled
      catch) must flush.

### Phase 4 — Consent, opt-out, first-run notice
- [ ] Add an `A365_TELEMETRY_OPTOUT` check (documented in README and `--help`).
- [ ] Add a one-time first-run telemetry notice (reuse the on-disk state pattern
      from `NoticeService` / `ConfigService.GetGlobalConfigDirectory()`), stating
      what is collected and how to opt out.
- [ ] Ensure the notice text is plain ASCII (repo rule: no emojis / special chars).

### Phase 5 — Docs, changelog, tests
- [ ] `CHANGELOG.md` `[Unreleased]`: one crisp consumer-facing sentence with the
      opt-out env var and a `(#NNN)` reference.
- [ ] README: telemetry section (what is collected, how to opt out).
- [ ] Tests (xUnit + FluentAssertions + NSubstitute):
  - Opt-out env var set -> `NoOpTelemetryService` selected, no events.
  - CI detected -> disabled.
  - No token compiled in -> disabled.
  - `Flush` is called on success, handled-error, and unhandled-exception exit paths.
  - Env-var tests use `[CollectionDefinition(DisableParallelization = true)]` and
    restore the variable in a `finally`.

## 6. Event schema (starter, PII/EUII-free)

One event to begin with; expand later only with review.

- Event: `Command.Invoked`
  - `commandName` (allowlisted verb, e.g. `setup`, `publish` — never raw args)
  - `exitCode` (int)
  - `durationMs` (long)
  - `cliVersion`
  - `os` (family only)
  - `isCi` (bool)

Explicitly excluded: file paths, tenant/subscription/object IDs, user or agent
names, argument values, secrets, tokens, environment variable contents.

## 7. Cross-platform and performance guardrails

- Managed 1DS SDK only (must run on Windows, macOS, Linux). No Windows-native
  client SDK.
- Telemetry init and flush must not add noticeable startup/exit latency; flush is
  bounded by a timeout and failures are swallowed (debug-logged only), like the
  existing notice/version checks.
- Telemetry failures must never change the CLI exit code or surface errors to users.

## 8. Open questions

- B1/B2/B3 above.
- Ingestion token: is it treated as a secret, or is it a low-sensitivity
  write-only key? Determines whether CI secret handling is required.
- Do we want per-command events or a single end-of-run event? (Start with one.)
- Retention window and data classification for the chosen tenant.

## 9. Explicitly out of scope

- Any `.cscfg` / `.csdef` / `WorkerRole` artifacts.
- Adding the internal ARIA-SDK feed to the shared `nuget.config`.
- Committing any ingestion token to the repository.
- Collecting any PII or EUII.
