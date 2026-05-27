// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// Automates a Copilot Chat conversation using Playwright.
/// Opens the M365 Chat page, selects an agent by name, sends a test message,
/// and waits for the agent to respond.
///
/// Auth strategy:
/// - Reuses the CLI's existing MSAL browser session. On Windows, WAM (Windows
///   Authentication Manager) automatically provides SSO for the M365 domain, so
///   the browser launched by Playwright inherits the user's session without any
///   manual login.
/// - On non-Windows platforms (or if SSO is not available), a headed browser
///   opens and waits for the user to log in manually.
/// - Saves browser storage state to a local file for reuse across validate runs.
///
/// The flow mirrors the Camp-AIR A365ObservabilityTests pattern:
///   auth/setup.ts for login, pages/chat-page.ts for interaction.
/// </summary>
public class CopilotChatPlaywrightService
{
    private readonly ILogger _logger;

    /// <summary>Base URL for M365 Chat.</summary>
    internal const string ChatBaseUrl = "https://m365.cloud.microsoft/chat";

    /// <summary>Timeout for agent response in milliseconds (2 minutes).</summary>
    internal const int AgentResponseTimeoutMs = 120_000;

    /// <summary>Timeout for page navigation in milliseconds (60 seconds).</summary>
    internal const int NavigationTimeoutMs = 60_000;

    /// <summary>
    /// Directory name under LocalApplicationData for storing auth state.
    /// Reuses the same directory as the CLI's MSAL token cache.
    /// </summary>
    private static readonly string AuthStateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Constants.AuthenticationConstants.ApplicationName);

    /// <summary>File name for the Playwright browser auth state.</summary>
    private const string AuthStateFileName = "playwright-auth-state.json";

    /// <summary>Auth state is reused if it is less than this many minutes old.</summary>
    private const int AuthStateTtlMinutes = 30;

    /// <summary>Streaming indicator phrases that signal the agent is still generating.</summary>
    private static readonly string[] StreamingPatterns = new[]
    {
        "Generating response",
        "Lining things up",
        "Working on it",
        "Thinking"
    };

    public CopilotChatPlaywrightService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a test message to the specified agent in Copilot Chat and returns the
    /// agent's response text, or null if the conversation could not be completed.
    /// </summary>
    /// <param name="agentName">Display name of the agent in M365 Chat.</param>
    /// <param name="testMessage">Message to send to the agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent's response text, or null if the conversation failed.</returns>
    public virtual async Task<string?> SendMessageToAgentAsync(
        string agentName,
        string testMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(testMessage);

        var authStatePath = GetAuthStatePath();

        // Install Playwright browsers if needed (first-run only)
        _logger.LogDebug("Ensuring Playwright browsers are installed...");
        var installExitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (installExitCode != 0)
        {
            _logger.LogWarning("Playwright browser install returned exit code {ExitCode}. Attempting to continue.", installExitCode);
        }

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        // Determine if we can reuse saved auth state
        var hasFreshAuthState = HasFreshAuthState(authStatePath);

        // Launch browser: headed if no saved state (user needs to log in), headless if reusing state
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = hasFreshAuthState,
            // Slow down actions slightly for stability with M365 UI
            SlowMo = hasFreshAuthState ? 0 : 100
        };

        _logger.LogDebug("Launching Chromium (headless: {Headless})...", launchOptions.Headless);
        await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);

        // Create context with saved state if available
        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        };
        if (hasFreshAuthState)
        {
            contextOptions.StorageStatePath = authStatePath;
        }

        await using var context = await browser.NewContextAsync(contextOptions);
        context.SetDefaultTimeout(30_000);
        context.SetDefaultNavigationTimeout(NavigationTimeoutMs);

        var page = await context.NewPageAsync();

        // Step 1: Navigate and authenticate
        _logger.LogDebug("Navigating to M365 Chat...");
        await page.GotoAsync(ChatBaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = NavigationTimeoutMs
        });

        // Check if we landed on the chat page or need to authenticate
        var chatInput = page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Message Copilot" });
        var isAuthenticated = await chatInput.IsVisibleAsync().ConfigureAwait(false);

        if (!isAuthenticated)
        {
            if (hasFreshAuthState)
            {
                _logger.LogDebug("Saved auth state did not work. Re-launching as headed for login...");
                // Close headless browser, reopen headed
                await page.CloseAsync();
                await context.CloseAsync();
                await browser.CloseAsync();

                return await RunWithInteractiveLoginAsync(playwright, agentName, testMessage, authStatePath, cancellationToken);
            }

            // Wait for the user to log in manually
            _logger.LogInformation("Please log in to M365 in the browser window.");
            _logger.LogInformation("The CLI will continue automatically once login completes.");

            // Wait for redirect to M365 chat (user completes login)
            await page.WaitForURLAsync("**/m365.cloud.microsoft/**",
                new PageWaitForURLOptions { Timeout = 300_000 });

            // Navigate to chat page to ensure all cookies are set
            await page.GotoAsync(ChatBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = NavigationTimeoutMs
            });

            // Wait for chat input to appear
            await chatInput.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30_000
            });

            _logger.LogInformation("Login successful.");
        }

        // Save auth state for next time
        await SaveAuthStateAsync(context, authStatePath);

        // Step 2: Open agent chat and send message
        var responseText = await OpenAgentChatAndSendMessageAsync(page, agentName, testMessage, cancellationToken);

        return responseText;
    }

    /// <summary>
    /// Fallback path: launch a headed browser for interactive login, then send message.
    /// </summary>
    private async Task<string?> RunWithInteractiveLoginAsync(
        IPlaywright playwright,
        string agentName,
        string testMessage,
        string authStatePath,
        CancellationToken cancellationToken)
    {
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 100
        };

        await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);
        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        };
        await using var context = await browser.NewContextAsync(contextOptions);
        context.SetDefaultTimeout(30_000);
        context.SetDefaultNavigationTimeout(NavigationTimeoutMs);

        var page = await context.NewPageAsync();

        await page.GotoAsync(ChatBaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = NavigationTimeoutMs
        });

        _logger.LogInformation("Please log in to M365 in the browser window.");
        _logger.LogInformation("The CLI will continue automatically once login completes.");

        // Wait for redirect to M365
        await page.WaitForURLAsync("**/m365.cloud.microsoft/**",
            new PageWaitForURLOptions { Timeout = 300_000 });

        await page.GotoAsync(ChatBaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = NavigationTimeoutMs
        });

        var chatInput = page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Message Copilot" });
        await chatInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });

        _logger.LogInformation("Login successful.");

        await SaveAuthStateAsync(context, authStatePath);

        return await OpenAgentChatAndSendMessageAsync(page, agentName, testMessage, cancellationToken);
    }

    /// <summary>
    /// Opens a new chat with the specified agent and sends a test message.
    /// Returns the agent's response text, or null if the response could not be extracted.
    /// </summary>
    private async Task<string?> OpenAgentChatAndSendMessageAsync(
        IPage page,
        string agentName,
        string testMessage,
        CancellationToken cancellationToken)
    {
        // Click the agent button in the sidebar
        _logger.LogDebug("Opening agent chat for '{AgentName}'...", agentName);
        var agentButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = agentName });
        await agentButton.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
        await agentButton.First.ClickAsync();
        await page.WaitForTimeoutAsync(2000);

        // Wait for the chat input to be ready
        var chatInput = page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Message Copilot" });
        await chatInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });

        // Click "New chat" to start a fresh conversation
        // The header button is typically the second "New chat" button (first is sidebar menuitem)
        var newChatButtons = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New chat" });
        var buttonCount = await newChatButtons.CountAsync();
        if (buttonCount > 1)
        {
            await newChatButtons.Nth(1).ClickAsync();
        }
        else if (buttonCount == 1)
        {
            await newChatButtons.First.ClickAsync();
        }
        await page.WaitForTimeoutAsync(2000);

        // Re-focus the chat input
        await chatInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
        await chatInput.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Type and send message
        _logger.LogDebug("Sending test message to agent...");

        // Click the paragraph inside the textbox to ensure focus (M365 Chat pattern)
        var paragraph = chatInput.Locator("p, [role='paragraph']");
        try
        {
            await paragraph.First.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
        }
        catch
        {
            await chatInput.ClickAsync();
        }

        await chatInput.FillAsync(testMessage);
        await page.WaitForTimeoutAsync(500);
        await chatInput.PressAsync("Enter");

        // Wait for agent response
        _logger.LogDebug("Waiting for agent response...");
        var responseText = await WaitForAgentResponseAsync(page, cancellationToken);

        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogWarning("Agent response was empty.");
            return null;
        }

        _logger.LogDebug("Agent responded with {Length} characters.", responseText.Length);
        return responseText;
    }

    /// <summary>
    /// Waits for the agent to finish responding and extracts the response text.
    /// Strategy (from Camp-AIR):
    ///   1. Wait for "Copy Response" button or feedback group (response has started)
    ///   2. Poll until streaming indicators ("Generating response", etc.) clear
    ///   3. Stabilize: wait for text to stop changing
    ///   4. Extract final text from the last non-user article element
    /// </summary>
    private async Task<string?> WaitForAgentResponseAsync(IPage page, CancellationToken cancellationToken)
    {
        var timeout = AgentResponseTimeoutMs;

        // Step 1: Wait for response to start
        var copyResponseButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Copy Response" });
        var feedbackGroup = page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex("feedback", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        var completionIndicator = copyResponseButton.Or(feedbackGroup);

        await completionIndicator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeout
        });

        // Step 2: Poll until streaming indicators clear
        var pollStart = Environment.TickCount64;
        while (Environment.TickCount64 - pollStart < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.WaitForTimeoutAsync(3000);

            var allArticles = page.GetByRole(AriaRole.Article);
            var count = await allArticles.CountAsync();
            var stillStreaming = false;

            for (var i = 0; i < count; i++)
            {
                string text;
                try
                {
                    text = await allArticles.Nth(i).InnerTextAsync() ?? string.Empty;
                }
                catch
                {
                    text = string.Empty;
                }

                if (StreamingPatterns.Any(p => text.Contains(p, StringComparison.Ordinal)))
                {
                    stillStreaming = true;
                    break;
                }
            }

            if (!stillStreaming)
            {
                break;
            }

            _logger.LogDebug("Agent still streaming, waiting...");
        }

        // Step 3: Stabilize -- wait for text to stop changing
        var previousText = string.Empty;
        for (var i = 0; i < 3; i++)
        {
            await page.WaitForTimeoutAsync(2000);
            var currentText = await ExtractLastAgentResponseAsync(page);
            if (currentText == previousText && !string.IsNullOrEmpty(currentText))
            {
                break;
            }
            previousText = currentText;
        }

        // Step 4: Extract final response
        return await ExtractLastAgentResponseAsync(page);
    }

    /// <summary>
    /// Extracts the text from the last non-user article element on the page.
    /// User messages start with "You said:"; agent responses do not.
    /// </summary>
    private static async Task<string> ExtractLastAgentResponseAsync(IPage page)
    {
        var allArticles = page.GetByRole(AriaRole.Article);
        var count = await allArticles.CountAsync();

        for (var i = count - 1; i >= 0; i--)
        {
            string text;
            try
            {
                text = await allArticles.Nth(i).InnerTextAsync() ?? string.Empty;
            }
            catch
            {
                continue;
            }

            if (text.StartsWith("You said:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Extract text after "said:" heading if present
            var saidIndex = text.IndexOf("said:", StringComparison.OrdinalIgnoreCase);
            if (saidIndex >= 0)
            {
                var afterSaid = text[(saidIndex + 5)..].Trim();
                if (!string.IsNullOrEmpty(afterSaid))
                {
                    return afterSaid;
                }
            }

            return text.Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Checks if saved auth state exists and is less than <see cref="AuthStateTtlMinutes"/> old.
    /// </summary>
    private bool HasFreshAuthState(string authStatePath)
    {
        if (!File.Exists(authStatePath))
        {
            _logger.LogDebug("No saved auth state found at {Path}.", authStatePath);
            return false;
        }

        var fileInfo = new FileInfo(authStatePath);
        var ageMinutes = (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalMinutes;
        if (ageMinutes < AuthStateTtlMinutes)
        {
            _logger.LogDebug("Auth state is {Age:F0} min old (< {Ttl} min). Reusing.", ageMinutes, AuthStateTtlMinutes);
            return true;
        }

        _logger.LogDebug("Auth state is {Age:F0} min old (>= {Ttl} min). Will re-authenticate.", ageMinutes, AuthStateTtlMinutes);
        return false;
    }

    /// <summary>
    /// Saves browser context storage state for reuse.
    /// </summary>
    private async Task SaveAuthStateAsync(IBrowserContext context, string authStatePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(authStatePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await context.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = authStatePath
            });
            _logger.LogDebug("Auth state saved to {Path}.", authStatePath);
        }
        catch (Exception ex)
        {
            // Non-fatal: failing to save just means the user re-authenticates next time
            _logger.LogDebug(ex, "Failed to save auth state: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Gets the path where Playwright auth state is stored.
    /// </summary>
    internal static string GetAuthStatePath()
    {
        return Path.Combine(AuthStateDirectory, AuthStateFileName);
    }
}
