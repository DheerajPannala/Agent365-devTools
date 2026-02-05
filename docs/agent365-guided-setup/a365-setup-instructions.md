# Agent 365 CLI Setup Instructions for AI Agents

## Context

You are an AI coding agent with access to execute shell commands, read the Agent365-devTools repository (code and docs), and browse the web for documentation or GitHub issues. Your task is to set up, configure, and deploy all prerequisite components for a Microsoft Agent 365–compliant agent using the Agent 365 CLI. You must handle this end-to-end: from installation and configuration to deployment. Work step-by-step, and adapt to any issues or differences in CLI versions along the way.

> **CRITICAL BLOCKING PREREQUISITE:** Before running ANY `a365` CLI commands (including `config init`, `setup`, `publish`, or `deploy`), you MUST validate that the custom client app registration exists in Entra ID with all required permissions and admin consent. This is validated in Step 2. Failure to validate this will cause all CLI commands to fail. Do NOT skip this validation step.

---

## Step 1: Verify and Install/Update the Agent 365 CLI

Check if the Agent 365 CLI is installed and up-to-date:

- Run a version check (e.g. `a365 --version` or `a365 -h`).
- If the CLI is not installed or the command is not found, you need to install it. If it is installed but the version is outdated, you should update it to the latest preview version.

### Ensure .NET is installed

The Agent 365 CLI is a .NET global tool. Verify that you have .NET 8.0 (or a compatible version) available by running `dotnet --version`. If not, instruct the user to install .NET 8.0 or install it yourself if you have the ability (the CLI cannot run without this).

### Install or update the Agent 365 CLI

Use the [official documentation](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/agent-365-cli#install-the-agent-365-cli) to install/update the CLI globally. Always include the `--prerelease` flag to get the latest preview:

- **If not installed:** run `dotnet tool install --global Microsoft.Agents.A365.DevTools.Cli --prerelease`
- **If an older version is installed:** run `dotnet tool update --global Microsoft.Agents.A365.DevTools.Cli --prerelease`
- **On Windows environments:** If the above command fails or if you prefer, you can use the provided PowerShell script from the repository to install the CLI. For example, run the `scripts/cli/install-cli.ps1` script (after uninstalling any existing version with `dotnet tool uninstall -g Microsoft.Agents.A365.DevTools.Cli`).

### Verify installation

After installing or updating, confirm the CLI is ready by running `a365 -h` to display help. This also ensures the CLI is on the PATH. It should show usage information rather than an error.

### Adapt to CLI version differences

The CLI is under active development, and some commands may have changed in recent versions. The instructions in this prompt assume you have the latest version. If you discover that a command referenced later (such as `publish`) is not recognized, it means you have an older version – in that case, upgrade the CLI. Using the latest version is essential because older flows (e.g. the `create-instance` command) have been deprecated in favor of new commands (`publish`, etc.). If upgrading isn't possible, adjust your steps according to the older CLI's documentation (for example, use the old `a365 create-instance` command in place of `publish`), but prefer to upgrade if at all feasible.

---

## Step 2: Ensure Prerequisites and Environment Configuration

### Azure CLI & Authentication

The Agent 365 CLI relies on Azure context for deploying resources and may use your Azure credentials. Verify that the Azure CLI (`az`) is installed by running `az --version`. If it's not available, install the [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) for your platform or prompt the user to do so.

If the Azure CLI is installed, ensure that you are logged in to the correct Azure account and tenant. Run `az login` (and `az account set -s <SubscriptionNameOrID>` if you need to select a specific subscription). If you cannot perform an interactive login directly, output a clear instruction for the user to log in (the user may need to follow a device-code login URL if running in a headless environment). The Agent 365 CLI will use this Azure authentication context to create resources.

### Microsoft Entra ID (Azure AD) roles

The user account you authenticate with must have sufficient privileges to create the necessary resources. According to documentation, the account needs to be at least an **Agent ID Administrator** or **Agent ID Developer**, and certain commands (like the full environment setup) require **Global Administrator + Azure Contributor** roles. If you attempt an operation without adequate permissions, it will fail. Thus, before proceeding, confirm that the logged-in user has one of the required roles (Global Admin is the safest choice for preview setups). If not, prompt the user to either use an appropriate account or have an admin grant the needed roles.

### Custom client app registration (BLOCKING - MUST VALIDATE BEFORE PROCEEDING)

> **CRITICAL BLOCKER:** The custom client app registration is a hard prerequisite. You MUST validate that the app exists and has all required permissions BEFORE running any `a365` CLI commands (including `a365 config init`, `a365 setup`, etc.). Skipping this validation will cause CLI commands to fail with authentication or permission errors.

Verify that a custom client application is registered in Entra ID (Azure AD) for Agent 365 authentication. This is critical for the CLI to function (it uses this app to manage Agent Identity Blueprints). The user should have created this app as part of the prerequisites.

#### Step 2.1: Validate the custom client app exists

You MUST perform this validation before proceeding. Use the Azure CLI to query for the app registration:

1. **If you have a known Application (client) ID** (e.g., from an existing `a365.config.json` file with the `clientAppId` field, or provided by the user), validate it exists:
   ```bash
   az ad app show --id <CLIENT_APP_ID> --query "{appId:appId, displayName:displayName}" -o table
   ```
   If this command returns the app details, proceed to Step 2.2 to validate permissions.
   If this command fails with "Resource not found" or similar, the app does not exist - see "Creating the app" below.

2. **If you do not have an Application (client) ID**, ask the user if it exists.
   If they say no or that they do not know, assume the custom app has not been created - see "Creating the app" below.
   If they say yes, prompt them for the app id. Once provided, validate it exists.
   ```bash
   az ad app show --id <CLIENT_APP_ID> --query "{appId:appId, displayName:displayName}" -o table
   ``` app list --display-name "Agent365" --query "[].{appId:appId, displayName:displayName}" -o table
   ```
   If this command returns the app details, proceed to Step 2.2 to validate permissions.
   If this command fails with "Resource not found" or similar, the app does not exist - see "Creating the app" below.

#### Step 2.2: Validate required Graph API permissions

Once you have confirmed the app exists, you MUST validate that it has all required delegated permissions with admin consent. Run:

```bash
az ad app show --id <CLIENT_APP_ID> --query "requiredResourceAccess"
```

The app registration must have the following **delegated (not application)** Microsoft Graph permissions, with **admin consent granted** for each:

| Permission | Description |
|------------|-------------|
| `AgentIdentityBlueprint.ReadWrite.All` | Manage Agent 365 Blueprints |
| `AgentIdentityBlueprint.UpdateAuthProperties.All` | Update Blueprint auth properties |
| `Application.ReadWrite.All` | Create and manage Azure AD applications |
| `DelegatedPermissionGrant.ReadWrite.All` | Grant delegated permissions |
| `Directory.Read.All` | Read directory data |

To check if admin consent has been granted, run:
```bash
az ad app permission list-grants --id <CLIENT_APP_ID> --query "[].{scope:scope}" -o table
```

If any permissions are missing or admin consent has not been granted, you MUST stop and resolve this before proceeding.

#### What to do if validation fails

**If the app does not exist or is missing required permissions:**

1. **STOP** - Do not proceed to run any `a365` CLI commands.
2. **Inform the user** that the custom client app registration is missing or incomplete.
3. **Direct the user** to follow the official setup guide to create/fix the app:
   - Register a new app in Azure AD (typically named "Agent365 CLI")
   - Configure it as a **Public client** with redirect URI `http://localhost:8400`
   - Add all five delegated permissions listed above
   - Have a **Global Administrator** grant admin consent for all permissions
4. **Wait** for the user to confirm the app is properly configured before continuing.

Do not attempt to automatically create this app via script unless explicitly authorized, because it requires admin privileges and specific consent.

#### Proceed only when validation passes

Only after you have confirmed:
- [ ] The custom client app exists (you have a valid `clientAppId`)
- [ ] All five required delegated permissions are configured
- [ ] Admin consent has been granted for all permissions

...may you proceed to Step 3 (Configure the Agent 365 CLI).

### Other prerequisites

Ensure that any language-specific tools needed for your agent's code are available. The Agent 365 CLI supports .NET, Node.js, and Python projects. Depending on the type of agent you are deploying, check that the relevant runtime or build tools are installed:

- **For .NET agents:** Check `dotnet --list-sdks` and ensure the required .NET SDK for building the project is available.
- **For Node.js agents:** Check `node --version` and `npm --version`. Ensure you can run a build (e.g., `npm install` and `npm run build` if applicable).
- **For Python agents:** Check `python --version` and `pip --version`. You should also ensure any Python dependencies (especially any Microsoft 365-related SDKs) can be installed (for example, run `pip install -r requirements.txt` in the project directory to verify).

If any required runtime or tool is missing, attempt to install it (if you have the ability), or instruct the user on how to install it. The deployment will fail if the agent's code can't be built or if required interpreters aren't present.

---

## Step 3: Configure the Agent 365 CLI (Initialize Configuration)

> **PREREQUISITE CHECK:** Before proceeding with this step, you MUST have completed the custom client app registration validation in Step 2. If you have not validated that the app exists and has all required permissions with admin consent, STOP and go back to Step 2.

Once all prerequisites are in place (CLI installed, Azure CLI logged in, **custom app validated**), create the Agent 365 CLI configuration file. The `a365 config init` command is non-interactive, so you must create an `a365.config.json` file directly and then import it.

### Pre-configuration checklist

Before creating the configuration, confirm you have:
- [ ] Validated the custom client app exists (Step 2.1)
- [ ] Validated all required permissions are granted with admin consent (Step 2.2)
- [ ] The Application (client) ID ready (from the validation step)

If any of these are not confirmed, do not proceed - go back to Step 2.

### Step 3.1: Gather auto-detected values

Retrieve the following values automatically using the Azure CLI:

```bash
# Get tenant ID and subscription ID
az account show --query "{tenantId:tenantId, subscriptionId:id}" -o json
```

You should already have the `clientAppId` from the Step 2 validation.

Set `deploymentProjectPath` to the current working directory (use absolute path).

### Step 3.2: Determine deployment type

Ask the user: **"Do you want to create a web app in Azure for this agent? (yes/no)"**

- If **yes**: This is an Azure-hosted deployment. Set `needDeployment: true`. You will need to ask for the `appServicePlanName`.
- If **no**: This is a non-Azure hosted deployment. Set `needDeployment: false`. Proceed to Step 3.2.1 to determine the messaging endpoint.

#### Step 3.2.1: Determine messaging endpoint for non-Azure deployments

For non-Azure hosted deployments, ask the user: **"Would you like to use a dev tunnel for local development, or provide a custom messaging endpoint? (devtunnel/custom)"**

- If **devtunnel**: Proceed to Step 3.2.2 to set up a dev tunnel. The dev tunnel URL will be used as the `messagingEndpoint`.
- If **custom**: Ask the user to provide their `messagingEndpoint` URL (e.g., `https://myagent.example.com/api/messages`).

#### Step 3.2.2: Set up a dev tunnel (for local development)

Dev tunnels provide a secure way to expose your local agent to the internet, which is useful for development and testing. Follow these steps to set up a dev tunnel:

##### Prerequisites for dev tunnels

1. **Install the Dev Tunnels CLI** (if not already installed):
   
   Check if the dev tunnel CLI is installed by running:
   ```bash
   devtunnel --version
   ```
   
   If not installed, install it using one of the following methods:
   
   - **Windows (winget):**
     ```bash
     winget install Microsoft.devtunnel
     ```
   
   - **Windows (PowerShell direct download):**
     ```powershell
     Invoke-WebRequest -Uri https://aka.ms/TunnelsCliDownload/win-x64 -OutFile devtunnel.exe
     ```
   
   - **macOS (Homebrew):**
     ```bash
     brew install --cask devtunnel
     ```
   
   - **Linux:**
     ```bash
     curl -sL https://aka.ms/DevTunnelCliInstall | bash
     ```

2. **Log in to dev tunnels:**
   ```bash
   devtunnel user login
   ```
   This will open a browser for authentication. Use your Microsoft account or Azure AD account.

##### Create and start the dev tunnel

1. **Determine the local port** your agent will run on. Ask the user: **"What local port will your agent run on? (default: 3978)"**
   
   Use the provided port or default to `3978` if not specified.

2. **Create a persistent (named) dev tunnel:**
   ```bash
   devtunnel create <agentName> --allow-anonymous
   ```
   Use the `agentName` value that the user provides in Step 3.3 as the tunnel name. This ensures consistency between the tunnel and agent naming. The `--allow-anonymous` flag allows the Agent 365 service to connect without additional authentication.
   
   > **Note:** Since the tunnel name depends on the `agentName` from Step 3.3, you should collect the agent name first (from Step 3.3), then return here to create the tunnel before completing the rest of Step 3.

3. **Add a port to the tunnel:**
   ```bash
   devtunnel port create <agentName> --port-number <local-port>
   ```
   Replace `<agentName>` with the agent name from Step 3.3 and `<local-port>` with the port from step 1 (e.g., `3978`).

4. **Get the tunnel URL:**
   ```bash
   devtunnel show <agentName>
   ```
   Look for the "Connect via browser" URL in the output. It will be in the format:
   `https://<tunnel-id>-<port>.devtunnels.ms`

5. **Construct the messaging endpoint:**
   Append your agent's message handler path to the tunnel URL. Typically this is `/api/messages`:
   ```
   https://<tunnel-id>-<port>.devtunnels.ms/api/messages
   ```
   
   This is your `messagingEndpoint` value.

6. **Start hosting the tunnel:**
   ```bash
   devtunnel host <agentName>
   ```

> **Important:** Keep this terminal window open while developing/testing. The tunnel will stop when this command is terminated.

> **Why use a persistent tunnel?** Persistent (named) tunnels maintain the same URL across sessions. This means you won't need to update your Agent 365 configuration every time you restart the tunnel. Simply run `devtunnel host <agentName>` to resume with the same endpoint URL.

> **Alternative (Temporary tunnel):** If you prefer a quick, temporary tunnel without persistence, you can use:
> ```bash
> devtunnel host --port-number <local-port> --allow-anonymous
> ```
> Note: The URL will change each time you run this command, requiring you to update the messaging endpoint in Agent 365.

##### Dev tunnel summary

After setting up the dev tunnel, you should have:
- [ ] Dev tunnel CLI installed and authenticated
- [ ] A persistent tunnel created using the `agentName` from Step 3.3
- [ ] The tunnel URL constructed as the `messagingEndpoint`
- [ ] The tunnel running with `devtunnel host <agentName>`

Proceed to Step 3.3 with the `messagingEndpoint` set to your dev tunnel URL.

### Step 3.3: Prompt user for required values

Prompt the user for the following information:

| Value | Description | Example |
|-------|-------------|---------|
| `resourceGroup` | Azure resource group name | `a365-rg` |
| `location` | Azure region | `eastus`, `canadacentral`, `westus2` |
| `agentName` | Unique name for the agent (used to derive identity, blueprint, webapp, and user names) | `mya365agent` |
| `managerEmail` | Email of the agent's manager | `admin@contoso.onmicrosoft.com` |

**For Azure-hosted deployments only** (`needDeployment: true`):
| Value | Description | Example |
|-------|-------------|---------|
| `appServicePlanName` | Name for the App Service Plan | `myagent-app-plan` |

**For non-Azure hosted deployments only** (`needDeployment: false`):
| Value | Description | Example |
|-------|-------------|---------|
| `messagingEndpoint` | The URL where your agent is hosted | `https://myagent.example.com/api/messages` |

### Step 3.4: Derive naming values from base name

Using the `agentBaseName` provided by the user and the domain extracted from `managerEmail`, derive the following values:

| Field | Pattern | Example (baseName=`mya365agent`, domain=`contoso.onmicrosoft.com`) |
|-------|---------|---------|
| `agentIdentityDisplayName` | `{baseName} Identity` | `mya365agent Identity` |
| `agentBlueprintDisplayName` | `{baseName} Blueprint` | `mya365agent Blueprint` |
| `agentUserPrincipalName` | `UPN.{baseName}@{domain}` | `UPN.mya365agent@contoso.onmicrosoft.com` |
| `agentUserDisplayName` | `{baseName} Agent User` | `mya365agent Agent User` |
| `agentDescription` | `{baseName} - Agent 365 Agent` | `mya365agent - Agent 365 Agent` |
| `webAppName` (Azure-hosted only) | `{baseName}-webapp` | `mya365agent-webapp` |

### Step 3.5: Confirm derived values with user

After deriving the values above, present them to the user and ask for confirmation. Display the derived values in a clear format:

**"Based on your inputs, the following values have been derived as defaults:"**

| Field | Derived Value |
|-------|---------------|
| `agentIdentityDisplayName` | `{baseName} Identity` |
| `agentBlueprintDisplayName` | `{baseName} Blueprint` |
| `agentUserPrincipalName` | `UPN.{baseName}@{domain}` |
| `agentUserDisplayName` | `{baseName} Agent User` |
| `agentDescription` | `{baseName} - Agent 365 Agent` |
| `webAppName` (if Azure-hosted) | `{baseName}-webapp` |

Then ask: **"Would you like to update any of these derived values, or proceed with the defaults? (update/proceed)"**

- If the user chooses **"proceed"**: Continue to Step 3.6 with the derived default values.
- If the user chooses **"update"**: Ask which field(s) they want to change and collect the new value(s). After updates, display the final values again for confirmation before proceeding.

### Step 3.6: Create the a365.config.json file

Create the `a365.config.json` file in the current working directory with all gathered and derived values.

**Template for Azure-hosted deployment** (`needDeployment: true`):

```json
{
  "tenantId": "<from az account show>",
  "subscriptionId": "<from az account show>",
  "resourceGroup": "<user provided>",
  "location": "<user provided>",
  "environment": "prod",
  "needDeployment": true,
  "clientAppId": "<from Step 2 validation>",
  "appServicePlanName": "<user provided>",
  "webAppName": "<derived from baseName>",
  "agentIdentityDisplayName": "<derived from baseName>",
  "agentBlueprintDisplayName": "<derived from baseName>",
  "agentUserPrincipalName": "<derived from baseName and domain>",
  "agentUserDisplayName": "<derived from baseName>",
  "managerEmail": "<user provided>",
  "agentUserUsageLocation": "US",
  "deploymentProjectPath": "<current working directory>",
  "agentDescription": "<derived from baseName>"
}
```

**Template for non-Azure hosted deployment** (`needDeployment: false`):

```json
{
  "tenantId": "<from az account show>",
  "subscriptionId": "<from az account show>",
  "resourceGroup": "<user provided>",
  "location": "<user provided>",
  "environment": "prod",
  "messagingEndpoint": "<user provided>",
  "needDeployment": false,
  "clientAppId": "<from Step 2 validation>",
  "agentIdentityDisplayName": "<derived from baseName>",
  "agentBlueprintDisplayName": "<derived from baseName>",
  "agentUserPrincipalName": "<derived from baseName and domain>",
  "agentUserDisplayName": "<derived from baseName>",
  "managerEmail": "<user provided>",
  "agentUserUsageLocation": "US",
  "deploymentProjectPath": "<current working directory>",
  "agentDescription": "<derived from baseName>"
}
```

### Step 3.7: Import the configuration

After creating the `a365.config.json` file, import it using:

```bash
a365 config init -c ./a365.config.json
```

### Validation

The `config init` process will attempt to validate your inputs. Notably, it will check:

- That the provided Application (client) ID corresponds to an existing app in the tenant and that it has the required permissions (the CLI might automatically verify the presence of the Graph permissions and admin consent). If this validation fails (for example, "app not found" or "missing permission X"), do not proceed further until the issue is resolved. Refer back to the app registration guide and fix the configuration (you may need the user's help to adjust the app's settings or wait for an admin consent).
- **Azure subscription and resource availability:** it might check that the subscription ID is accessible and you have Contributor rights (if you logged in via Azure CLI, this should be okay).
- It could also test the project path for a recognizable project (looking for a `.csproj`, `package.json`, or `pyproject.toml` to identify .NET/Node/Python). If it warns that it "could not detect project platform" or similar, double-check the `deploymentProjectPath` you provided. If it's wrong, update it and re-import the configuration.

If any validation fails, correct the `a365.config.json` file and re-run `a365 config init -c ./a365.config.json`.

### Proceed when config is successful

Once `a365 config init` completes without errors, you have a baseline configuration ready. The CLI now knows your environment details and is authenticated. This configuration will be used by subsequent commands.

---

## Step 4: Run Agent 365 Setup to Provision Prerequisites

With the CLI configured, the next major step is to set up the cloud resources and Agent 365 blueprint required for your agent. The CLI provides a one-stop command to do this:

### Execute the setup command

Run `a365 setup all`. This single command performs all the necessary setup steps in sequence. Under the hood, it will:

- Create or validate the Azure infrastructure for the agent (Resource Group, App Service Plan, Web App, and enabling a system-assigned Managed Identity on the web app).
- Create the Agent 365 Blueprint in your Microsoft Entra ID (Azure AD). This involves creating an Azure AD application (the "blueprint") that represents the agent's identity and blueprint configuration. The CLI uses Microsoft Graph API for this.
- Configure the blueprint's permissions (for MCP and for the bot/App Service). This likely entails granting certain API permissions or setting up roles so that the agent's identity can function (for example, granting the blueprint the ability to have "inheritable permissions" or other settings, which requires Graph API operations).
- Register the messaging endpoint for the agent's integration (this ties the web application to the Agent 365 service so that Teams and other Microsoft 365 apps can communicate with the agent).

In summary, "setup all" carries out what used to be multiple sub-commands (`setup infrastructure`, `setup blueprint`, `setup permissions mcp`, `setup permissions bot`, etc.), so running it will perform a comprehensive initial setup.

### Monitor the output

This command may take a few minutes as it provisions cloud resources and does Graph API calls. Monitor the console output carefully:

- The CLI will log progress in multiple steps (often numbered like `[0/5]`, `[1/5]`, etc.). Watch for any errors or warnings. Common points of failure include: Azure resource creation issues (quota exceeded, region not available, etc.), or Graph permission issues when creating the blueprint (e.g. insufficient privileges causing a "Forbidden" or "Authorization_RequestDenied" error).
- If the CLI outputs a warning about Azure CLI using 32-bit Python on 64-bit system (on Windows) or similar performance notices, you can note them but they don't block execution — they just suggest installing a 64-bit Azure CLI for better performance. This is not critical for functionality.
- If resource group or app services already exist (maybe from a previous run or a partially completed setup), the CLI will usually detect them and skip creating duplicates, which is fine.

### Important considerations

- **Quota limits:** If you see an error like "Operation cannot be completed without additional quota" during App Service plan creation, that means the Azure subscription has hit a quota limit (for example, no free capacity for new App Service in that region or SKU). In this case, you might need to change the region or service plan SKU, or have the user request a quota increase. This is an Azure issue, not a CLI bug. Report this clearly to the user and halt, or try choosing a different region if possible (you would need to update the config's `location` and possibly rerun setup).
- **Region support:** If you see errors related to Azure region support (for instance, an error about an Azure resource not available in region), recall that Agent 365 preview might support only certain regions for Bot Service or other components. If that happens, choose a supported region (update your `a365.config.json` with a supported `location` and run `a365 setup all` again).
- **Graph API permission errors:** If there are Graph API permission errors while creating the blueprint (e.g., a "Forbidden" error creating the application or setting permissions), this likely indicates the account running the CLI lacks a required directory role or the custom app's permissions aren't correctly consented. For example, an error containing "Authorization_RequestDenied" or mention of missing `AgentIdentityBlueprint` permissions suggests the custom app might not have those delegated permissions with admin consent. In such a case, stop and resolve the permission issue (see Step 2). You may need to have a Global Admin grant the consent or use an account with the appropriate role. After fixing, you can retry `a365 setup all`.
- **Interactive authentication during setup:** The CLI might attempt to do an interactive login to Azure AD (especially for granting some permissions or acquiring tokens for Graph). If running in a headless environment, this could fail (e.g., you see an error about `InteractiveBrowserCredential` or needing a GUI window). The CLI should ideally use the Azure CLI token, but for certain Graph calls (like `AgentIdentityBlueprint.ReadWrite.All` which might not be covered by Azure CLI's token), it might launch a browser auth. If this happens, see troubleshooting below for how to handle interactive auth in a non-interactive setting.

### Completion of setup

If `a365 setup all` completes successfully, you should see a confirmation in the output. It typically indicates that the blueprint is created and the messaging endpoint is registered. The CLI might output important information such as: the Agent Blueprint Application ID it created, or any Consent URLs for adding additional permissions. For instance, sometimes after setup, the CLI might provide a URL for admin consent (though if the custom app was properly set up with consent, ideally this isn't needed). If any consent URL or similar is printed, make sure to surface that to the user with an explanation (e.g., "The CLI is asking for admin consent for additional permissions; please open the provided URL in a browser and approve it as a Global Admin, then press Enter to continue."). The CLI may pause until consent is granted in such cases.

### Note on Idempotency

You can generally re-run `a365 setup all` if something went wrong and you fixed it. The CLI is designed to skip or reuse existing resources, as seen in the logs (e.g., resource group already exists, etc.). So don't hesitate to run it again after addressing an issue. If for some reason you need to start over, the CLI provides a cleanup command (`a365 cleanup`) to remove resources, but use that with caution (it can delete a lot). It's usually not necessary unless you want to wipe everything and retry from scratch.

---

## Step 5: Publish and Deploy the Agent Application

At this stage, your environment (Azure infrastructure and identity blueprint) is set up. Next, you need to publish the agent and deploy the application code so that the agent is live.

### Publish the agent manifest

Run `a365 publish`. This step updates the agent's manifest identifiers and publishes the agent package to Microsoft Online Services (specifically, it registers the agent with the Microsoft 365 admin center under your tenant). What this does:

- It takes your project's `manifest.json` (which should define your agent's identity and capabilities) and updates certain IDs in it (the CLI will inject the Azure AD application IDs – the blueprint and instance IDs – where needed).
- It then publishes the agent manifest/package to your tenant's catalog (so that the agent can be "hired" or installed in Teams and other apps).

Watch for output messages. Successful publish will indicate that the agent manifest is updated and that you can proceed to create an instance of the agent. If there's an error during publish, read it closely. For example, if the CLI complains about being unable to update some manifest or reach the admin center, ensure your account has the necessary privileges and that the custom app registration has the permissions for `Application.ReadWrite.All` (since publish might call Graph to update applications). Also, ensure your internet connectivity is good.

### Deploy the agent code to Azure

Run `a365 deploy`. This will take the agent's application (the code project you pointed to in the config) and deploy it to the Azure Web App that was set up earlier. Specifically, `a365 deploy` will typically:

- Build your project (if it's .NET or Node, it will compile or bundle the code; if Python, it might collect requirements, etc.).
- Package the build output and deploy it to the Azure App Service (the web app). This could be via zip deploy or other Azure deployment mechanism automated by the CLI.
- Ensure that any required application settings (like environment variables, or any connection info) are configured. (For example, the CLI might convert a local `.env` to Azure App Settings for Python projects, as noted in its features.)
- It will also finalize any remaining permission setups (for instance, adding any last-minute Microsoft 365 permissions through the Graph if needed for the agent's operation; the CLI documentation mentions "update Agent 365 Tool permissions," which likely happens here or in publish).

**Note:** If you only want to deploy code without touching permissions (say, on subsequent iterations), the CLI offers subcommands `a365 deploy app` (just deploy binaries) and `a365 deploy mcp` (update tool permissions). But in a first-time setup, just running the full `a365 deploy` is fine, as it covers everything.

Monitor this process. If the build fails (maybe due to code issues or missing build tools), address the build error (you might need to install additional dependencies or fix a build script). If the deployment fails (e.g., network issues uploading, or Azure App Service issues), note the error and retry as needed.

On success, the CLI will indicate that the application was deployed. You should now have an Azure Web App running your agent's code.

### Post-deployment (User action required)

Once deployed, the agent's backend is live. At this point, from the perspective of the CLI, the agent is set up. However, there are additional steps to fully activate the agent in the Microsoft 365 environment: configuring the agent in Teams Developer Portal and creating an agent instance.

> **Important:** The following post-deployment steps must be completed by the user manually. These steps require browser-based interactions with the Teams Developer Portal and Microsoft Teams that cannot be automated by an AI agent. Provide the user with these instructions so they can complete them on their own.

For complete details, see [Create agent instances](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/create-instance).

#### Step 1: Configure agent in Teams Developer Portal (User action)

**Instruct the user** to configure the agent blueprint in Teams Developer Portal to connect their agent to the Microsoft 365 messaging infrastructure. Without this configuration, the agent won't receive messages from Teams, email, or other Microsoft 365 services.

Provide the user with the following instructions:

1. **Get your blueprint app ID** by running:
   ```bash
   a365 config display -g
   ```
   Copy the `agentBlueprintAppId` value from the output.

2. **Navigate to Developer Portal** by opening your browser and going to:
   ```
   https://dev.teams.microsoft.com/tools/agent-blueprint/<your-blueprint-app-id>/configuration
   ```
   Replace `<your-blueprint-app-id>` with the value you copied.

3. **Configure the agent** in the Developer Portal:
   - Set **Agent Type** to `Bot Based`
   - Set **Bot ID** to your `agentBlueprintAppId` value
   - Select **Save**

> **Note:** If the user doesn't have access to the Developer Portal, they should contact their tenant administrator to grant access or complete this configuration on their behalf.

#### Step 2: Create agent instance (User action)

**Instruct the user** to request an instance of the agent blueprint from Teams. For more details, see [How to discover, create, and onboard an agent](https://learn.microsoft.com/en-us/microsoft-agent-365/onboard).

Provide the user with the following instructions:

1. Open **Teams > Apps** and search for your agent name
2. Select your agent and click **Request Instance** (or **Create Instance**)
3. Teams sends the request to your tenant admin for approval

Admins can review and approve requests from the [Microsoft admin center - Requested Agents](https://admin.cloud.microsoft/#/agents/all/requested) page. After approval, Teams creates the agent instance and makes it available.

> **Important:** The user needs to be part of the [Frontier preview program](https://adoption.microsoft.com/copilot/frontier-program/) to create agent instances and interact with agents in Microsoft Teams while Agent 365 is in preview. They should contact their tenant administrator if they don't have access.

#### Step 3: Test your deployed agent (User action)

**Instruct the user** to test the agent instance in Microsoft Teams after it's created:

1. Search for the new agent user in Teams
   > **Note:** The agent user creation process is asynchronous and can take a few minutes to a few hours for the agent user to become searchable after it's created.

2. Start a new chat with the newly created agent instance

3. Send test messages to verify agent functionality (e.g., "Hello!")

4. If tools are configured (e.g., Email MCP server), test tool functionality

**View the agent in the admin center:** Go to the [Microsoft 365 admin center - Agents](https://admin.cloud.microsoft/#/agents/all) to view the published agent, manage settings, monitor usage, and configure permissions.

**Check application logs** (for Azure-hosted deployments):
```bash
az webapp log tail --name <your-web-app> --resource-group <your-resource-group>
```

If your agent instance isn't working as expected, see the Troubleshooting section below or the [Agent 365 Troubleshooting Guide](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/troubleshooting).

---

## Error Handling and Troubleshooting

Things don't always go smoothly. As an AI agent, you must be prepared to diagnose and fix issues at each stage. Here's how to systematically troubleshoot:

### General approach

If any step results in an error or unexpected output, stop and analyze the error message carefully. Cross-reference the error with available documentation or known issues. Utilize all resources at your disposal: the official Microsoft Learn docs for Agent 365 CLI, the content of the Agent365-devTools GitHub repository (including its README, usage guide, and any scripts or code), and the GitHub issues (both open and closed) for this project. Often, others have encountered similar problems, and maintainers' responses in issue threads can provide solutions or workarounds.

### Installation issues

**NuGet or network errors during `dotnet tool install`:** If the CLI installation fails with an error about retrieving the package (for example, "NuGet package not found" or connectivity issues), ensure internet access is available. The `Microsoft.Agents.A365.DevTools.Cli` package is hosted on NuGet; a common issue when the CLI was just released was needing the `--prerelease` flag (which we already include). Verify that you included `--prerelease`. If the error persists, try again after a short wait (NuGet may have been temporarily unreachable). If there is a persistent version resolution issue, you can search the GitHub issues; for instance, one issue reported an installation glitch that was resolved in later versions. Upgrading dotnet SDK or clearing NuGet caches might help in some cases.

**CLI command not found after installation:** If `a365` still isn't found after a successful install, ensure that the dotnet tools path is in the system PATH. You may need to manually add it or restart the shell. By default on Windows, it's in `%USERPROFILE%\.dotnet\tools`, and on Linux/Mac in `~/.dotnet/tools`. If the agent environment doesn't pick up changes to PATH, you might have to call the binary via its full path.

### Azure CLI / Authentication issues

If commands fail because you are not logged in (for example, an error explicitly saying you need to login or "No subscription found"), run `az login` and ensure the correct subscription.

If `a365 setup` or other commands attempt to do an interactive login (for Graph) and fail in a headless environment (e.g., error: "InteractiveBrowserCredential authentication failed: A window handle must be configured" or any mention of `InteractiveBrowserCredential`), this is a known limitation in non-interactive terminals. Workarounds include:

- Ensure you have the latest CLI version, as improvements might be made to support device code flow. (Check the release notes or issues if such a feature is available, e.g., an issue suggests a `--use-device-code` flag or automatic fallback might be introduced.) If such an option exists, try running the command with that flag to force a device code authentication (which will output a code for the user to enter at https://microsoft.com/devicelogin).
- If no such option in the CLI, you can attempt to manually pre-authenticate: For example, use the PowerShell Microsoft Graph module or Azure CLI to obtain tokens. However, the CLI may not reuse those for the specific Graph scope it needs (as noted in an issue, the CLI spawned its own process that didn't reuse the parent token cache). In short, the robust solution is likely beyond your direct control. Therefore, the best approach is to inform the user that the operation requires interactive login. For instance, instruct: "This command needs to open a browser to acquire a Graph token. Please run it in an environment where a web browser is available, or use a local machine instead of a headless server for this step." You might also mention that a future CLI update may address this, and reference the relevant issue if appropriate.
- If the issue persists and blocks progress, treat it as a potential bug (see "Escalating to GitHub" below). 

If `a365 setup` fails at the "setup permissions mcp" stage with an authentication error, this is likely the same issue as above (needing an interactive login for the delegated permissions to configure the MCP – Model Context Protocol – server permissions). The workaround until it's fixed would be the same: use an interactive environment or file a bug.

### Graph permission or consent issues

An error containing "Failed to acquire token" or "insufficient privileges" or anything about authorization failed during setup or publish indicates something amiss with the Graph permissions setup. Double-check that the custom app registration's delegated permissions are exactly as required and that admin consent has been granted. You might retrieve the current permissions via Azure Portal or Azure AD PowerShell to confirm. If a permission was missed or not consented, add/consent it and try again.

If the CLI specifically prints a URL for admin consent (often the case if it tried to do something and realized you need tenant-wide approval), make sure the user (Global Admin) completes that step. The CLI logs or error might mention which permission was lacking when it failed. Provide guidance to the user on granting that permission. Once done, re-run the failed command.

### Azure provisioning issues

**Resource already exists:** If you run `a365 setup all` multiple times, you might see warnings or errors about existing resources (for example, if you had partially run it before). The CLI is generally idempotent, but in case some resource is in a bad state, you may use CLI or Azure Portal to inspect it. For instance, if a web app was created but endpoint registration failed, you might delete that web app manually (or use `a365 cleanup azure`) and then retry setup. Only use `a365 cleanup` as a last resort because it will delete many things (it's meant to remove everything the CLI created).

**Quotas and limits:** As mentioned, if you hit a quota, the error message from Azure will indicate which resource type. The user might need to free up or raise the quota. A quick alternative is to try deploying to a different Azure region or SKU that has available capacity (update the config and run `a365 setup all` again).

**Unsupported region or service:** If the error implies something like "The region is not supported" for an Azure resource (especially likely for Azure Bot Service or related to Teams integration), consult the documentation or known issues for supported regions. The 2025 preview limited certain features (e.g., bot registration) to specific regions. Changing the region to one of the known working ones (as noted earlier) can resolve this.

### Application deployment issues

**Build failures:** If `a365 deploy` fails while building the project, the error will usually show in the console (like MSBuild errors for .NET, or npm errors for Node). Solve these as you would normally: check that all project files are correct, all dependencies are listed, etc. You can attempt to build outside the CLI to replicate the issue (e.g., run `dotnet build` or `npm run build` manually) to get more detail. Address code issues or missing dependencies accordingly.

**Python-specific:** If deploying a Python agent and it fails to detect or install dependencies, ensure that your project has a `requirements.txt` or `pyproject.toml` that lists Agent 365 SDK and others. The CLI tries to convert local `.env` to Azure settings; ensure your environment variables are set in config or `.env` so it picks them up.

**Publish folder not found:** If you used the `--restart` flag on deploy (to skip rebuild) and hit "publish folder not found," it means no previous build output is present. Simply run `a365 deploy` without `--restart` at least once to generate the publish folder, or ensure the `deploymentProjectPath` is correct. We addressed this earlier; follow the fix of doing a full deploy first.

### Dev tunnel issues

If you are using a dev tunnel for local development, you may encounter the following issues:

**Dev tunnel CLI not found:** If `devtunnel` command is not found after installation, ensure the installation completed successfully and the binary is in your PATH. On Windows, you may need to restart your terminal or add the installation directory to PATH manually. Try running the full path to the executable or reinstalling.

**Authentication failures:** If `devtunnel user login` fails or times out, ensure you have a stable internet connection and that your browser can open. If running in a headless environment, use device code authentication:
```bash
devtunnel user login --device-code
```
This will provide a code to enter at https://microsoft.com/devicelogin.

**Tunnel connection issues:** If the tunnel is created but the agent cannot receive messages:
- Verify the tunnel is actively running (`devtunnel host <tunnel-name>` must be running in a terminal).
- Confirm the local port matches what your agent is listening on.
- Check that `--allow-anonymous` was used when creating the tunnel (required for Agent 365 service connectivity).
- Test the tunnel URL in a browser - you should see a response from your local agent (or a connection refused if the agent isn't running).

**Tunnel URL has changed:** If you used a temporary tunnel or recreated your named tunnel, the URL may have changed. Update the messaging endpoint by running:
```bash
a365 setup blueprint --update-endpoint https://<new-tunnel-id>-<port>.devtunnels.ms/api/messages
```

> **Tip:** Using a persistent (named) tunnel avoids this issue. The URL remains consistent across sessions, so you only need to run `devtunnel host <tunnel-name>` to resume.

**Port already in use:** If you see an error that the port is already in use when hosting the tunnel:
```bash
devtunnel port delete <tunnel-name> --port-number <old-port>
devtunnel port create <tunnel-name> --port-number <new-port>
```

**Tunnel expires or disconnects:** Free-tier dev tunnels may have usage limits or timeout after extended periods of inactivity. If your tunnel stops working:
- Re-run `devtunnel host <tunnel-name>` to restart it.
- Consider upgrading to a paid tier for production-like scenarios, or switch to Azure-hosted deployment for long-running agents.

**Cannot access tunnel from Teams:** If the Agent 365 service or Teams cannot reach your tunnel:
- Ensure the tunnel was created with `--allow-anonymous` flag.
- Verify your firewall allows outbound connections to `*.devtunnels.ms`.
- Check that your local agent is running and listening on the correct port.
- Confirm the full messaging endpoint URL is correct (including the `/api/messages` path or your agent's specific endpoint path).

### Using the repository and docs for insight

The Agent365-devTools repo contains a `Readme-Usage.md` (which we have effectively followed) and possibly other docs in the `docs/` folder. If a certain command is not behaving as expected, consider reading the relevant section in those docs or the CLI reference in Microsoft Learn. For example, if uncertain how a subcommand works, you can run `a365 <command> --help` for quick info, or check the `docs/commands/` directory in the repo for detailed reference markdown files.

Search the GitHub issues by error message. If you encounter "ERROR: Web app creation failed" or "Failed to configure XYZ," search those phrases. Often you will find an issue thread where maintainers offer a workaround or it might indicate the bug is fixed in a newer version (prompting you to update the CLI if you haven't).

If you suspect the issue is in the CLI's logic, you can even browse the source code (in `src/`) to understand what it's trying to do. For instance, if a certain property isn't being applied, the source might reveal it. This is advanced and usually not required unless diagnosing a bug for reporting.

### Escalating to GitHub (Drafting an Issue)

If you have exhausted the troubleshooting steps and it appears that the problem is due to a bug or unimplemented feature in the Agent 365 CLI itself (not a user error or missing prerequisite), then prepare to create a GitHub issue for the maintainers. Examples might include: a crash or unhandled exception in the CLI, a scenario where the CLI's behavior contradicts the documentation, or an inability to proceed due to a limitation in the tool.

Before writing a new issue, quickly search the existing issues (open and closed) to see if it's already reported. If it is, you might find temporary fixes or at least avoid duplicate reporting. If you have additional details to contribute, you can plan to mention them in the existing issue thread instead of opening a new one.

**Collect information for the issue:** Gather the relevant details:

- **Descriptive title:** Summarize the problem in a concise way (e.g., "a365 setup all fails to acquire Graph token in headless environment" or "Error XYZ during deploy on Linux"). This will be the issue title.
- **Environment details:** Note the CLI version (`a365 --version` output), OS platform and version, shell or environment (PowerShell, Bash on Ubuntu, etc.), and any other relevant environment info (Azure CLI version if relevant, or whether you're using a headless server or behind a proxy, etc.).
- **Steps to reproduce:** Write down the exact sequence of commands you ran and in what context that led to the issue. Be as precise as possible, including any configuration choices that might matter. The goal is for the maintainers (or any developer) to replicate the issue easily. Example: "1. Installed CLI v1.0.49, 2. Ran a365 config init with app ID X... 3. Ran a365 setup all in a Windows Terminal on Windows 10, user is Global Admin, Azure region WestUS, 4. Saw error ..."
- **The actual error message and logs:** Copy the relevant error output. If it's long, you can provide the tail of the log or the key error snippet. The logs are also available in files (`a365.setup.log`, etc., as noted in the Readme-Usage). You can open and include sections of those log files if they provide more detail than the console output. Make sure to remove or redact any sensitive info (like GUIDs that might be tenant or subscription IDs, if needed). Usually, error messages and stack traces are safe to share.
- **Expected behavior:** Describe what you expected to happen if the bug was not present (e.g., "the command should complete without errors and the agent should be set up" or "the token acquisition should fall back to device code instead of failing"). This helps clarify the discrepancy.
- **Workarounds attempted:** List any steps you tried to resolve it (e.g., "I tried re-running with --verbose, tried manual token generation, but none worked"). This helps the maintainers know what you've already done and not suggest the same things again.
- **Potential cause or fix (optional):** If you have insight from the error or code, include your thoughts. For example, "It appears the CLI does not support device code flow for Graph auth in headless scenarios. Perhaps adding `-UseDeviceAuthentication` to the PowerShell `Connect-MgGraph` call would solve this." Even if you're not 100% sure, this shows you did homework and could speed up the fix. If you have identified exactly where in the code the problem is and can suggest a specific code change, that's even better. For instance, "The error comes from A365SetupRunner when calling the Graph SDK. Catching the exception and retrying with a device code might resolve it." (Keep a respectful tone and frame it as a suggestion.)

Once you compile this information, format it as a new GitHub issue. Follow the style of existing issues: start with a brief description, then headings like "To Reproduce", "Expected behavior", "Actual behavior" (or "Error details"), and "Environment". Attach log excerpts or screenshots if helpful (text is preferable for logs).

**Do not actually create the GitHub issue on your own** (unless explicitly authorized). Instead, present the draft to the user or maintainers. For example, you can output: "Draft Issue Report: ...". This allows the user to review and post it themselves, or gives the maintainers the info if they are following along.

If the bug is blocking your progress and there's no workaround, gracefully stop after providing the draft issue and explanation. It's better to wait for a fix or guidance than to continue in a broken state. In your communication with the user, emphasize that the issue appears to be on the tool's side and that you've prepared a report for the developers.

### Logging and verbosity

If you need more information while troubleshooting, remember that many `a365` commands support a `-v` or `--verbose` flag (as shown in help messages). For example, `a365 setup all -v` might output more detailed logs. Use this when an operation fails without enough info; the extra logs could reveal the failing step. Also, you can check the log files mentioned in the Readme (e.g., `~/.config/a365/logs/a365.setup.log` on Linux/Mac or the AppData path on Windows) for more detail. Include relevant parts of these logs in your analysis or in the GitHub issue if one is being filed.

### Reverting changes

In some cases, you might want to undo partial changes (for example, if the deployment got half-way and you want to clean up before retrying). The CLI's `a365 cleanup` commands can remove resources: `a365 cleanup azure` to delete Azure components, `a365 cleanup blueprint` to remove the Entra ID application (blueprint), etc. Use them carefully and only if you plan to fully retry the setup or if you want to roll back everything. If only a minor fix is needed, it's usually not necessary to clean up; you can just re-run the failing step.

### Reference official documentation

Throughout the process, if you are unsure how to proceed or want to verify the proper usage of a command, refer to the official documentation on Microsoft Learn. The main pages of interest are:

- **Agent 365 CLI overview and installation** – provides info on prerequisites and install/update process.
- **Agent 365 CLI Reference** – lists all commands and options in detail.
- **Specific command reference pages** – e.g., "setup" command, "config" command, "deploy" command references, which detail what each sub-step does and any options or requirements.
- **Custom client app registration guide** – details how to do the Entra ID app setup (we summarized it above).

These docs can be accessed online (links were given) or might be included in the repository's docs folder. Use them as needed to double-check correct behavior.

---

By following the above steps and using thorough troubleshooting practices, you should be able to successfully guide the Agent 365 CLI through installing all prerequisites, configuring the environment, and deploying the agent. Always prioritize resolving any errors before moving on to the next step, to ensure a smooth setup. Once completed, confirm with the user that the agent is up and running, and provide any final instructions (like how to interact with the agent in Teams or where to find logs for the running agent).
