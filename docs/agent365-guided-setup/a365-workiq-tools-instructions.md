# Add Work IQ Tools (MCP Integration)

> **SCOPE — THIS FILE ONLY:** This file covers configuring Work IQ MCP (Model Context Protocol)
> tool integrations for an Agent 365 agent using the `a365 develop` and `a365 setup permissions mcp`
> CLI commands. It does **NOT** cover initial agent provisioning or observability setup. If the agent
> has not yet been provisioned, open `a365-setup-instructions.md` first.
>
> This file is used in two ways: (1) automatically, as a follow-on step in
> `a365-setup-instructions.md` when the selected capabilities include Work IQ; (2) directly, when
> the user explicitly asks to add Work IQ tools or MCP integrations to an already-provisioned agent.

**Prerequisite:** `a365 setup blueprint` or `a365 setup all` must have completed successfully.
The agent must have a valid `agentBlueprintId` in `a365.generated.config.json` before MCP
permissions can be configured.

---

## Context

Work IQ gives your agent access to Microsoft 365 MCP tool servers — Mail, Calendar, Teams,
SharePoint, OneDrive, Knowledge, Office documents, and more. The Agent 365 CLI configures OAuth2
permission grants between your agent's blueprint identity and each requested MCP server's Entra app.

**The integration has three parts:**
1. Discover which MCP servers are available using `a365 develop list-available`.
2. Add chosen servers to a `ToolingManifest.json` file using `a365 develop add-mcp-servers`.
3. Apply OAuth2 permission grants using `a365 setup permissions mcp`.

---

## Task A — Discover and select MCP servers

### A.1 — List available MCP servers

Run the following command to fetch the live catalog from the Agent 365 Tools service:

```bash
a365 develop list-available
```

This queries the discovery endpoint and prints every available MCP server with its name, URL,
required scope, and audience. It also saves a local catalog cache used by subsequent commands.

Display the full output to the user and ask:

**"Which MCP servers would you like to enable for your agent? List the server names exactly as
shown above (e.g. `MCP_MailTools MCP_CalendarTools`), or reply `all` to enable every server
listed."**

Wait for the user's response. Store as `selectedServers`.

If `a365 develop list-available` fails (auth error, network issue):
- Confirm `az login --allow-no-subscriptions` has been run and `az account show` returns a valid
  account.
- Retry. Do not proceed until the catalog is successfully fetched.

### A.2 — Add selected servers to ToolingManifest.json

From the project directory, run:

```bash
cd "<project_dir>" && a365 develop add-mcp-servers <server1> <server2> ...
```

Replace `<server1> <server2> ...` with the exact server names the user selected. The command reads
the catalog (or re-fetches it if the cache is stale), resolves scope and audience automatically,
and writes or updates `ToolingManifest.json` in the project directory.

**Example:**
```bash
a365 develop add-mcp-servers MCP_MailTools MCP_CalendarTools MCP_TeamsTools
```

If the user replied `all`, pass every server name from the `a365 develop list-available` output.

### A.3 — Verify the manifest

Confirm the servers were written correctly:

```bash
cd "<project_dir>" && a365 develop list-configured
```

This reads `ToolingManifest.json` and prints each configured server with its scope, audience, and
protocol version (V1/V2). Show the output to the user. If any expected server is missing, re-run
`a365 develop add-mcp-servers` for the missing name.

---

## Task B — Apply MCP permissions via CLI

### B.1 — Dry-run preview (REQUIRED — do not skip)

> **Safety check. You MUST show the dry-run output to the user before applying.**

```bash
cd "<project_dir>" && a365 setup permissions mcp --dry-run
```

Display the full output, then ask:

**"Do you want to apply the MCP permission grants shown above? (yes/no)"**

- If **no**: stop. Tell the user "MCP permissions not applied. Return when ready."
- If **yes**: proceed to B.2.

### B.2 — Apply MCP permission grants

```bash
cd "<project_dir>" && a365 setup permissions mcp
```

This command reads `ToolingManifest.json`, creates OAuth2 permission grants between the agent
blueprint and each MCP server's Entra app, and sets inheritable permissions so all agent instances
inherit MCP access.

Monitor output carefully. Common issues:

| Error | Cause | Fix |
|-------|-------|-----|
| `Blueprint ID not found` | `a365 setup all` not run, or blueprint deleted | Run `a365 setup all` first |
| `Forbidden / 403` | Insufficient directory role | Have Global Admin grant consent (see B.3) |
| `ToolingManifest.json not found` | File missing or wrong path | Re-run Task A from `project_dir` |
| `Unknown scope … Re-run: a365 develop add-mcp-servers` | Server name not in catalog | Run `a365 develop list-available` and use an exact name from the output |

### B.3 — Admin consent (if required)

If `a365 setup permissions mcp` reports that admin consent is required, the CLI will print either
a PowerShell script or an Entra admin consent portal link. Show the user exactly what the CLI
printed and say verbatim:

> "Work IQ requires admin consent to grant MCP permissions to your agent. Have a Global Admin
> complete one of the options above, then confirm back when done."

Do not proceed until the user confirms consent is granted. Then re-run to verify:

```bash
cd "<project_dir>" && a365 setup permissions mcp
```

---

## Task C — Verify MCP integration

Run the following to confirm the MCP scopes appear on the blueprint configuration:

```bash
a365 config display -g
```

The output should include the configured MCP server entries and their scopes. If any expected server
is missing, re-check the manifest with `a365 develop list-configured` and re-run Task B.

### Task C completion — final summary

> **REQUIRED.** After Task C completes, output a single summary in this format and nothing else:
>
> **Work IQ MCP integration complete.**
>
> **MCP servers enabled** _(list each server name and its scope from `a365 develop list-configured`
> output):_
> - `<mcpServerName>` — `<scope>`
> - ...
>
> **Next steps:**
> - Test tool access by sending a message that triggers an MCP tool call (e.g., "Check my email"
>   for `MCP_MailTools`).
> - If a tool call fails with a 401 or permission error, re-run `a365 setup permissions mcp` and
>   verify admin consent completed.

Do NOT add commentary or further output after this summary.

---

## Task D — Update MCP servers (ongoing)

**To add more servers:**
```bash
cd "<project_dir>" && a365 develop add-mcp-servers <new-server-name>
cd "<project_dir>" && a365 setup permissions mcp
```

**To remove servers:**
```bash
cd "<project_dir>" && a365 develop remove-mcp-servers <server-name>
cd "<project_dir>" && a365 setup permissions mcp
```

Both commands are idempotent — safe to re-run. Permissions are reconciled to match whatever
is in `ToolingManifest.json` at the time the command runs.

### Migrating from V1 to V2 permissions

If the agent was originally set up with the V1 shared audience model — where all MCP servers share
a single resource app ID (`ea9ffc3e-8a23-4a7d-836d-234d7c7565c1`) and scopes follow the
`McpServers.*.All` pattern (e.g. `McpServers.Mail.All`, `McpServers.Teams.All`) — and you are
migrating to per-server V2 permissions (each server has its own app ID, scope `Tools.ListInvoke.All`),
use `--remove-legacy-scopes` **only after all agent instances have been upgraded to the latest SDK**:

```bash
cd "<project_dir>" && a365 setup permissions mcp --remove-legacy-scopes
```

> **Caution:** This permanently removes the shared ATG audience scopes from the blueprint. Agent
> instances still on the V1 SDK will immediately lose MCP tool access. The CLI will prompt for
> explicit confirmation before proceeding.

---

## Task E — Wire MCP tool consumption in agent code

> **agentType = 1 (M365 custom engine agents):** The Agent 365 platform handles MCP tool dispatch
> natively. No code changes are required — skip this task.
>
> **agentType = 2 (all other agents):** The CLI work in Tasks A–C configures Entra permissions.
> For the agent to actually call Work IQ tools at runtime, the Microsoft Agent 365 tooling SDK
> must be installed and wired into the agent's turn handler. Follow the steps below for your
> platform.

### E.1 — Install the tooling SDK

**Python** (run in your project's virtual environment):
```bash
uv add microsoft_agents_a365_tooling microsoft_agents_a365_tooling_extensions_openai
# or with pip:
pip install microsoft-agents-a365-tooling microsoft-agents-a365-tooling-extensions-openai
```

**Node.js:**
```bash
npm install @microsoft/agents-a365-tooling @microsoft/agents-a365-tooling-extensions-openai
```

**.NET (Agent Framework):**
```bash
dotnet add package Microsoft.Agents.A365.Tooling.Extensions.AgentFramework --prerelease
```

### E.2 — Register services at startup

**.NET — add to `Program.cs`:**
```csharp
using Microsoft.Agents.A365.Tooling.Extensions.AgentFramework.Services;
using Microsoft.Agents.A365.Tooling.Services;

builder.Services.AddSingleton<IMcpToolRegistrationService, McpToolRegistrationService>();
builder.Services.AddSingleton<IMcpToolServerConfigurationService, McpToolServerConfigurationService>();
```

**Python / Node.js:** no startup registration needed — instantiate the services directly in the
agent class.

### E.3 — Wire MCP tools per turn

**Python:**
```python
from microsoft_agents_a365.tooling.services.mcp_tool_server_configuration_service import (
    McpToolServerConfigurationService,
)
from microsoft_agents_a365.tooling.extensions.openai import mcp_tool_registration_service

class MyAgent:
    def __init__(self):
        self.tool_service = mcp_tool_registration_service.McpToolRegistrationService()

    async def on_message(self, auth, auth_handler_name, context):
        # Production (agentic auth):
        await self.tool_service.add_tool_servers_to_agent(
            agent=self.agent,
            auth=auth,
            auth_handler_name=auth_handler_name,
            context=context,
        )
        # Local dev fallback (bearer token):
        # await self.tool_service.add_tool_servers_to_agent(
        #     agent=self.agent, auth=auth,
        #     auth_handler_name=auth_handler_name, context=context,
        #     auth_token=os.getenv("BEARER_TOKEN"),
        # )
```

**Node.js:**
```typescript
import { McpToolRegistrationService } from '@microsoft/agents-a365-tooling-extensions-openai';

const toolService = new McpToolRegistrationService();

// In your message handler — per turn:
await toolService.addToolServersToAgent(
    agent,
    authorization,
    authHandlerName,
    turnContext,
    process.env.BEARER_TOKEN ?? "",  // empty string = use agentic auth
);
```

**.NET:**
```csharp
// Inject into your agent class constructor:
public MyAgent(IMcpToolRegistrationService toolService, ...)
{
    _toolService = toolService;
}

// Per turn — GetMcpToolsAsync returns List<AITool> to add to ChatOptions:
var tools = await _toolService.GetMcpToolsAsync(
    agentId,
    UserAuthorization,
    authHandlerName,      // null → falls back to BEARER_TOKEN env var
    turnContext,
    tokenOverride: null   // pass BEARER_TOKEN string value here for local dev
);
// Add tools to your ChatOptions.Tools list before calling the LLM
```

### E.4 — Environment variables

| Variable | Purpose |
|----------|---------|
| `BEARER_TOKEN` | Local dev — shared fallback bearer token used when agentic auth is not available |
| `BEARER_TOKEN_<SERVER_NAME_UPPER>` | Local dev — per-server bearer token for V2 per-audience model. The SDK resolves this by uppercasing the `mcpServerName` from `ToolingManifest.json` (e.g. `mcp_MailTools` → `BEARER_TOKEN_MCP_MAILTOOLS`, `mcp_CalendarTools` → `BEARER_TOKEN_MCP_CALENDARTOOLS`). Only set this for servers that are present in your manifest — unused entries have no effect. |
| `USE_AGENTIC_AUTH` | Python: set `true` to force the agentic auth path (ignores `BEARER_TOKEN`) |
| `SKIP_TOOLING_ON_ERRORS` | Dev only — set `true` to fall back to bare LLM if MCP tools fail to load (only honoured when `ASPNETCORE_ENVIRONMENT` / `ENVIRONMENT` = `Development`) |

> **Production:** Use the agentic auth handler (configured in `appsettings.json` /
> `a365.config.json`). `BEARER_TOKEN` and `BEARER_TOKEN_<SERVER_NAME_UPPER>` are for local
> development only — never set them in a production deployment.

### Task E completion

> **REQUIRED.** After completing Task E, update the final summary from Task C to include:
>
> **SDK wiring** _(list each file modified and the call added):_
> - `<File>` — `<method added>` (e.g. `add_tool_servers_to_agent`, `GetMcpToolsAsync`)

---

## Advanced — Dataverse MCP server management

Use `a365 develop-mcp` when your agent **publishes its own MCP server** to the Microsoft admin
center (for other agents to consume). This is separate from consuming existing M365 MCP servers
covered above.

```bash
# List all Dataverse environments
a365 develop-mcp list-environments

# List MCP servers in a specific environment
a365 develop-mcp list-servers --environment-id <env-id>

# Publish an MCP server to an environment
a365 develop-mcp publish \
  --environment-id <env-id> \
  --server-name <name> \
  --alias <alias> \
  --display-name "<display name>"

# Unpublish, approve, or block an MCP server
a365 develop-mcp unpublish --environment-id <env-id> --server-name <name>
a365 develop-mcp approve --server-name <name>
a365 develop-mcp block --server-name <name>

# Generate a Teams app package for admin center submission
a365 develop-mcp package-mcp-server --server-name <name>
```

Add `--dry-run` to any command to preview changes. Add `--verbose` / `-v` for detailed logs.

---

## Troubleshooting

- Run failing commands with `-v` / `--verbose` for detailed logs.
- Log files: Windows `%APPDATA%/a365/logs/`, Linux/Mac `~/.config/a365/logs/`.
- `a365 setup permissions mcp` is idempotent — safe to re-run after fixing any issue.
- For CLI bugs, check [GitHub Issues](https://github.com/microsoft/Agent365-devTools/issues) and
  include: CLI version (`a365 --version`), OS/shell, exact error output.
