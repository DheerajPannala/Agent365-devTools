# Creating an Aria (1DS) Project for the `a365` CLI

How to create the Aria/1DS project that produces the **ingestion token** ("project
key" / "app token") this CLI uses, and how to wire that token into **this**
configuration. Companion to [telemetry-integration-plan.md](telemetry-integration-plan.md),
[telemetry-TODO.md](telemetry-TODO.md), and
[telemetry-cross-platform-TODO.md](telemetry-cross-platform-TODO.md).

> Portal navigation changes over time. The repo-specific values below are exact; treat
> the portal click-path as the standard flow and confirm against the current internal
> Aria/1DS onboarding portal.

## What an Aria project gives you

An Aria project issues a **project key** (also called the ingestion token or app
token). Your app passes it to `LogManager.Initialize(token)` and every event is routed
to that project. In this repo the token is consumed here:

| Aria concept | Value in this repo | Where |
| --- | --- | --- |
| Project key / ingestion token | `TelemetryAppToken` (currently empty placeholder) | [../src/Microsoft.Agents.A365.DevTools.Cli/Constants/ConfigConstants.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Constants/ConfigConstants.cs) |
| Runtime token override | env var `A365_TELEMETRY_TOKEN` | same file |
| Opt-out switch | env var `A365_TELEMETRY_OPTOUT` (`1`/`true`) | same file |
| Event source / logger id | `a365cli` | [../src/Microsoft.Agents.A365.DevTools.Cli/Services/TelemetryService.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Services/TelemetryService.cs) |
| Event name (once per invocation) | `cli_command_invoked` | `ConfigConstants` |
| Event properties | `command`, `version` | [Program.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Program.cs) |
| SDK | `Microsoft.Applications.Telemetry.Server-NetStandard` | [../src/Directory.Packages.props](../src/Directory.Packages.props) |

## Prerequisites

- Access to the internal Aria / 1DS onboarding portal.
- Permission to create a project in the target Aria tenant (or an owner who can).
- The platform decision for this app: **Azure | C#** — the managed .NET SDK. (Not the
  Windows UWP/Win32 SDKs; see the cross-platform note below.)

## Step 1 — Sign in and create the project

1. Sign in to the Aria/1DS portal with your corporate account.
2. Create a new **project** (sometimes shown as "tenant"/"title"). Provide:
   - A descriptive **name** (e.g. `Agent365 DevTools CLI`).
   - **Owners** (more than one, so the project is not orphaned).
   - **Data classification / retention** appropriate for CLI usage telemetry.

## Step 2 — Select the platform

- When prompted "What platform are you using?", choose **Azure | C#** — the managed
  cross-platform .NET SDK whose API (`LogManager` / `EventProperties` /
  `FlushAndTearDown`) matches [TelemetryService.cs](../src/Microsoft.Agents.A365.DevTools.Cli/Services/TelemetryService.cs).
- Do not pick Windows UWP/Win32 (native, Windows-only) — the CLI must run on
  Windows/macOS/Linux. See [telemetry-cross-platform-TODO.md](telemetry-cross-platform-TODO.md)
  for the non-Windows caveats with the current Server SDK.

## Step 3 — Copy the project key (ingestion token)

- After the project is created, copy its **project key**. It looks like:
  ```
  0123456789abcdef0123456789abcdef-01234567-0123-0123-0123-0123456789ab-0123
  ```
  (32-hex tenant token, a GUID, and a trailing sequence.)
- This is the value `LogManager.Initialize(token)` needs.

## Step 4 — Wire the token into this CLI (do not commit it)

`TelemetryAppToken` is intentionally empty today (`// TODO: Add Aria token here`).
Supply the token without committing it to this public repo:

- **Local / development:** set the environment variable, which overrides the compiled
  constant:
  ```powershell
  $env:A365_TELEMETRY_TOKEN = "<your-project-key>"
  ```
  ```bash
  export A365_TELEMETRY_TOKEN="<your-project-key>"
  ```
- **Official release builds:** inject the token at pack/build time (e.g. an MSBuild
  property fed from a pipeline secret that generates the constant), so it ships only in
  the official package and stays empty in public/dev builds.
- **Never** paste the token into `ConfigConstants.cs` and commit it. See the security
  note below.

To turn telemetry off entirely:
```
A365_TELEMETRY_OPTOUT=1
```

## Step 5 — Validate events are arriving

1. With `A365_TELEMETRY_TOKEN` set, run any command, e.g. `a365 --help`, then another
   real command so `cli_command_invoked` is emitted and flushed on exit.
2. In the Aria portal, open the project's data viewer / real-time monitor.
3. Filter by **event source** `a365cli` and **event name** `cli_command_invoked`.
4. Confirm the `command` and `version` properties appear as expected.

Events can take a few minutes to surface. If nothing arrives, check the CLI's debug log
(run with `-v`) for `Telemetry initialized.` and `Telemetry event ... queued.` lines.

## Security note

- A real project key was previously committed to this repo and is still in git history.
  It must be **rotated** in the Aria tenant — removing it from the file is not enough.
- Keep the ingestion token out of source control. Use `A365_TELEMETRY_TOKEN` or
  build-time injection only.

## Related

- [telemetry-integration-plan.md](telemetry-integration-plan.md) — overall design and the feed/token decisions.
- [telemetry-TODO.md](telemetry-TODO.md) — remaining work and ship-blockers.
- [telemetry-cross-platform-TODO.md](telemetry-cross-platform-TODO.md) — non-Windows support and definition of done.
