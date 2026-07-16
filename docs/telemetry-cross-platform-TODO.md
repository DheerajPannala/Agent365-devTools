# Telemetry TODO — Cross-Platform (non-Windows) Support & Completion

Scope: making `a365` CLI telemetry work correctly on Linux and macOS, and the full
"definition of done." Companion to [telemetry-TODO.md](telemetry-TODO.md) (which
covers the security/feed ship-blockers). Read both together.

Status legend: `[ ]` not done, `[x]` done.

## 1. Why telemetry is effectively Windows-only today

- The current SDK is the **Aria Server SDK**
  (`Microsoft.Applications.Telemetry.Server-NetStandard`), referenced in
  [../src/Directory.Packages.props](../src/Directory.Packages.props).
- It has an explicit runtime dependency on **`System.Diagnostics.PerformanceCounter`**,
  which is **Windows-only** — its APIs throw `PlatformNotSupportedException` on
  Linux/macOS.
- `TelemetryService.Initialize` / `TrackEvent` / `FlushAndShutdown` in
  [../src/Microsoft.Agents.A365.DevTools.Cli/Services/TelemetryService.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Services/TelemetryService.cs)
  wrap every call in try/catch and swallow failures, and there is **no OS guard**.

Net effect: on non-Windows the SDK likely throws during init, the exception is
caught, and telemetry silently never starts. The CLI does not crash, but:

- Non-Windows users send **zero** telemetry (a large blind spot for a cross-platform
  dev tool), and it fails **silently** so no one notices.
- Every non-Windows invocation may pay a failed-init cost and a misleading
  "cross-platform telemetry" impression.

## 2. Verify actual behavior first (decision gate)

- [ ] Run the CLI on **Linux** and **macOS** (container or CI matrix) and capture
      debug logs for `Initialize` / `TrackEvent` / `FlushAndShutdown`.
- [ ] Confirm whether init throws `PlatformNotSupportedException`, whether any event
      reaches the collector, and whether flush blocks.
- [ ] Record findings here; they select Option A vs B below.

## 3. Options to support non-Windows

### Option A — Explicit Windows-only guard (quick stop-gap, lossy)
Make the Windows-only limitation intentional instead of a silent caught exception.

- [ ] Add `if (!OperatingSystem.IsWindows()) return;` (or
      `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`) at the top of
      `TelemetryService.Initialize`.
- [ ] Document that telemetry is Windows-only for now.
- Pros: trivial, safe, no non-Windows cost. Cons: **no Linux/macOS data.**

### Option B — Cross-platform 1DS via OpenTelemetry OneCollector (recommended target)
Send to the same 1DS/OneCollector endpoint using a fully cross-platform, **public**
package — this also removes the internal feed and the committed-token problems from
[telemetry-TODO.md](telemetry-TODO.md).

- [ ] Add `OpenTelemetry` + `OpenTelemetry.Exporter.OneCollector` (both on nuget.org)
      to central package management; remove the Aria Server SDK, `Bond.*`,
      `System.Runtime.Caching`, and `System.Diagnostics.PerformanceCounter` references.
- [ ] Remove the internal `Aria` feed and its package-source mapping from
      [../src/nuget.config](../src/nuget.config) so public `dotnet restore` works.
- [ ] Re-implement `TelemetryService` on the OneCollector exporter while keeping the
      **same `ITelemetryService` surface** (`Initialize` / `TrackEvent` /
      `FlushAndShutdown`) so no callers change.
- [ ] Supply the instrumentation key/token via build-time injection or env var
      (never committed).
- [ ] Use `ForceFlush(timeoutMs)` on exit (cross-platform, bounded).
- Pros: works on Windows/Linux/macOS, public feed, no secret in source.
  Cons: larger change; validate the exporter API and endpoint/token format.

### Option C — Prove the Server SDK is cross-platform
Only if section 2 shows events actually flow on Linux/macOS:

- [ ] Drop or guard the `System.Diagnostics.PerformanceCounter` reference and confirm
      init/send/flush still succeed on all OSes. (Least likely to be clean.)

Recommendation: ship **Option A** immediately so behavior is explicit and honest,
and pursue **Option B** as the real cross-platform solution.

## 4. Cross-platform test matrix

- [ ] CI runs telemetry tests on `windows-latest`, `ubuntu-latest`, `macos-latest`.
- [ ] Unit test: on non-Windows, telemetry is a clean no-op (no surfaced exception,
      no hang) — for Option A.
- [ ] Integration: confirm an event arrives from each OS — for Option B.
- [ ] Verify `FlushAndShutdown` never hangs on any OS (bounded by timeout).

## 5. Definition of Done (conclude fully)

Telemetry is "done" when all of the following hold:

- [ ] Ingestion token removed from source **and rotated** in the Aria tenant (it is
      in git history — see [telemetry-TODO.md](telemetry-TODO.md)).
- [ ] A clean external clone can `dotnet restore` and build with **no internal feed
      and no token**.
- [ ] Telemetry works on **Windows, Linux, and macOS** — or is an explicit,
      documented no-op where unsupported (Option A).
- [ ] Telemetry is suppressed in CI/CD (`VersionCheckHelper.IsRunningInCiCd()`).
- [ ] First-run consent notice shown; `A365_TELEMETRY_OPTOUT` documented in README
      and `--help`.
- [ ] Flush is bounded and never delays or blocks CLI exit on any OS.
- [ ] Unit tests cover: opt-out disables, missing token disables, CI disables,
      non-Windows no-op, flush called on success / handled-error / unhandled-exception
      exit paths, and no PII in event properties.
- [ ] `CHANGELOG.md` `[Unreleased]` has a one-line consumer-facing entry with the
      opt-out env var and a `(#NNN)` reference.
- [ ] Event schema is documented and PII/EUII-free (`command` is an allowlisted verb,
      plus `version`; no paths, IDs, args, or secrets).
- [ ] Cross-platform CI matrix is green.

## 6. Open decisions

- Is non-Windows telemetry data actually required? If yes, Option B is mandatory; if
  no, Option A is sufficient and much cheaper.
- OneCollector exporter vs Server SDK (drives feed, token format, and API).
- Token sensitivity and injection mechanism (build-time secret vs env var).
