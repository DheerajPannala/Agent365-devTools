// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// Automates a conversation with an agent in Microsoft Teams using Playwright.
/// Opens Teams web, searches for the agent by name in a new chat, sends a test
/// message, and waits for the agent to respond.
///
/// Auth strategy:
/// - Reuses the CLI's existing MSAL browser session. On Windows, WAM (Windows
///   Authentication Manager) automatically provides SSO for the Teams domain, so
///   the browser launched by Playwright inherits the user's session without any
///   manual login.
/// - On non-Windows platforms (or if SSO is not available), a headed browser
///   opens and waits for the user to log in manually.
/// - Saves browser storage state to a local file for reuse across validate runs.
/// </summary>
public class CopilotChatPlaywrightService
{
    private readonly ILogger _logger;

    /// <summary>Base URL for Microsoft Teams web.</summary>
    internal const string ChatBaseUrl = "https://teams.microsoft.com";

    /// <summary>URL pattern indicating the user has successfully authenticated into Teams.</summary>
    internal const string AuthenticatedUrlPattern = "**/teams.microsoft.com/**";

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

    public CopilotChatPlaywrightService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a test message to the specified agent in Teams web and returns the
    /// agent's response text, or null if the conversation could not be completed.
    /// </summary>
    /// <param name="agentName">Display name of the agent (bot) in Teams.</param>
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

        _logger.LogDebug("Ensuring Playwright browsers are installed...");
        var installExitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (installExitCode != 0)
        {
            _logger.LogWarning("Playwright browser install returned exit code {ExitCode}. Attempting to continue.", installExitCode);
        }

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        var hasFreshAuthState = HasFreshAuthState(authStatePath);

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 100
        };

        _logger.LogDebug("Launching Chromium (headed)...");
        await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);

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

        _logger.LogInformation("Navigating to Teams web...");
        await page.GotoAsync(ChatBaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = NavigationTimeoutMs
        });

        // Check if we landed on Teams or need to authenticate
        var isAuthenticated = await IsTeamsLoadedAsync(page);

        if (!isAuthenticated)
        {
            if (hasFreshAuthState)
            {
                _logger.LogDebug("Saved auth state did not work. Re-launching as headed for login...");
                await page.CloseAsync();
                await context.CloseAsync();
                await browser.CloseAsync();

                return await RunWithInteractiveLoginAsync(playwright, agentName, testMessage, authStatePath, cancellationToken);
            }

            _logger.LogInformation("Please log in to Teams in the browser window.");
            _logger.LogInformation("The CLI will continue automatically once login completes.");

            await page.WaitForURLAsync(AuthenticatedUrlPattern,
                new PageWaitForURLOptions { Timeout = 300_000 });

            // Wait for Teams to fully load after redirect
            await WaitForTeamsReadyAsync(page);

            _logger.LogInformation("Login successful.");
        }

        await SaveAuthStateAsync(context, authStatePath);

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
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = NavigationTimeoutMs
        });

        _logger.LogInformation("Please log in to Teams in the browser window.");
        _logger.LogInformation("The CLI will continue automatically once login completes.");

        await page.WaitForURLAsync(AuthenticatedUrlPattern,
            new PageWaitForURLOptions { Timeout = 300_000 });

        await WaitForTeamsReadyAsync(page);

        _logger.LogInformation("Login successful.");

        await SaveAuthStateAsync(context, authStatePath);

        return await OpenAgentChatAndSendMessageAsync(page, agentName, testMessage, cancellationToken);
    }

    /// <summary>
    /// Checks if the Teams web app has loaded by looking for the left rail or chat UI.
    /// </summary>
    private async Task<bool> IsTeamsLoadedAsync(IPage page)
    {
        try
        {
            // Teams v2 uses a left app bar with Chat, Activity, etc.
            var chatNavItem = page.Locator("[data-tid='app-bar-86fcd49b-61a2-4701-b771-54728cd291fb']")
                .Or(page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chat" }))
                .Or(page.Locator("[data-tid='app-bar-chat']"));

            // Wait briefly for the chat nav to appear, then check visibility
            await chatNavItem.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for the Teams web UI to become ready after authentication.
    /// </summary>
    private async Task WaitForTeamsReadyAsync(IPage page)
    {
        // Wait for the app bar or chat section to appear, indicating Teams has loaded
        var chatNavItem = page.Locator("[data-tid='app-bar-86fcd49b-61a2-4701-b771-54728cd291fb']")
            .Or(page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chat" }))
            .Or(page.Locator("[data-tid='app-bar-chat']"));

        await chatNavItem.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    /// <summary>
    /// Opens a chat with the agent in Teams using the top search bar, sends a test
    /// message, and returns the agent's response text.
    /// </summary>
    private async Task<string?> OpenAgentChatAndSendMessageAsync(
        IPage page,
        string agentName,
        string testMessage,
        CancellationToken cancellationToken)
    {
        // Step 1: Use the top search bar to find the agent
        _logger.LogInformation("Searching for agent '{AgentName}' via search bar...", agentName);
        var searchBox = page.Locator("[data-tid='searchbox']")
            .Or(page.GetByRole(AriaRole.Search))
            .Or(page.GetByPlaceholder("Search"));

        try
        {
            await searchBox.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
            await searchBox.First.ClickAsync();
            await page.WaitForTimeoutAsync(500);
            await page.Keyboard.TypeAsync(agentName, new KeyboardTypeOptions { Delay = 50 });
            await page.WaitForTimeoutAsync(3000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not use the search bar: {Message}", ex.Message);
            await CaptureScreenshotAsync(page, "teams-no-search-bar");
            return null;
        }

        // Step 2: Click the matching result from the search dropdown
        _logger.LogInformation("Selecting agent from search results...");
        var searchResult = page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = agentName })
            .Or(page.GetByRole(AriaRole.Listitem).Filter(new LocatorFilterOptions { HasText = agentName }));

        try
        {
            await searchResult.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000
            });
            await searchResult.First.ClickAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Agent '{AgentName}' was not found in search results: {Message}", agentName, ex.Message);
            await CaptureScreenshotAsync(page, "teams-agent-not-found");
            return null;
        }

        await page.WaitForTimeoutAsync(3000);

        // Step 3: Type and send the message in the compose box
        _logger.LogInformation("Sending test message to agent...");
        var composeBox = page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Type a message" })
            .Or(page.Locator("[data-tid='ckeditor-replyConversation']"))
            .Or(page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Type a new message" }));

        try
        {
            await composeBox.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
            await composeBox.First.ClickAsync();
            await page.WaitForTimeoutAsync(500);

            await composeBox.First.FillAsync(testMessage);
            await page.WaitForTimeoutAsync(500);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not find or fill the compose box: {Message}", ex.Message);
            await CaptureScreenshotAsync(page, "teams-no-compose-box");
            return null;
        }

        // Record message count before sending so we can detect new messages
        var messagesBefore = await CountVisibleMessagesAsync(page);
        _logger.LogDebug("Messages visible before sending: {Count}", messagesBefore);

        // Click the send button or press Enter
        var sendButton = page.Locator("[data-tid='newMessageCommands-send']")
            .Or(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }));

        if (await sendButton.First.IsVisibleAsync().ConfigureAwait(false))
        {
            await sendButton.First.ClickAsync();
        }
        else
        {
            await composeBox.First.PressAsync("Enter");
        }

        _logger.LogInformation("Message sent, waiting for agent response...");

        // Step 4: Wait for agent response (new message must appear)
        var responseText = await WaitForNewMessageAsync(page, messagesBefore, cancellationToken);

        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogWarning("Agent response was empty.");
            await CaptureScreenshotAsync(page, "teams-empty-response");
            return null;
        }

        _logger.LogInformation("Agent responded with {Length} characters.", responseText.Length);
        return responseText;
    }

    /// <summary>
    /// Captures a screenshot for debugging when a step fails.
    /// Saved to the CLI's local data directory.
    /// </summary>
    private async Task CaptureScreenshotAsync(IPage page, string stepName)
    {
        try
        {
            var screenshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Constants.AuthenticationConstants.ApplicationName,
                "screenshots");
            Directory.CreateDirectory(screenshotDir);

            var fileName = $"{stepName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
            var filePath = Path.Combine(screenshotDir, fileName);

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = filePath, FullPage = true });
            _logger.LogInformation("Screenshot saved: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture screenshot for step '{Step}'.", stepName);
        }
    }

    /// <summary>
    /// Counts visible message elements in the chat pane.
    /// Uses multiple selector strategies to find message containers in Teams.
    /// </summary>
    private static async Task<int> CountVisibleMessagesAsync(IPage page)
    {
        // Teams renders messages in divs with data-tid="chat-pane-message" or similar
        var selectors = new[]
        {
            "[data-tid='chat-pane-message']",
            "[data-tid='messageListItem']",
            ".message-body-content",
        };

        foreach (var selector in selectors)
        {
            var count = await page.Locator(selector).CountAsync();
            if (count > 0)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>
    /// Waits for a new message to appear after the user's message was sent.
    /// Polls the page for new message elements and waits for the response text to stabilize.
    /// Returns null if no new message appears within the timeout.
    /// </summary>
    /// <summary>
    /// Common placeholder patterns that agents send before the real response.
    /// These are short acknowledgements that should be ignored.
    /// </summary>
    private static readonly string[] PlaceholderPatterns = new[]
    {
        "got it", "working on it", "let me", "thinking", "one moment",
        "just a moment", "hold on", "processing", "looking into",
        "give me a moment", "i'm on it", "sure thing"
    };

    private async Task<string?> WaitForNewMessageAsync(
        IPage page,
        int messageCountBefore,
        CancellationToken cancellationToken)
    {
        var timeout = AgentResponseTimeoutMs;
        var pollStart = Environment.TickCount64;
        var pollIntervalMs = 3000;

        // Phase 1: Wait for any new message to appear beyond our sent message
        _logger.LogDebug("Waiting for new messages (had {Before} before)...", messageCountBefore);

        var newMessageDetected = false;
        while (Environment.TickCount64 - pollStart < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentCount = await CountVisibleMessagesAsync(page);
            if (currentCount >= messageCountBefore + 2)
            {
                newMessageDetected = true;
                _logger.LogDebug("New message detected (count: {Before} -> {Current}).", messageCountBefore, currentCount);
                break;
            }

            var elapsed = (Environment.TickCount64 - pollStart) / 1000;
            _logger.LogInformation("Waiting for agent response... ({Elapsed}s)", elapsed);
            await page.WaitForTimeoutAsync(pollIntervalMs);
        }

        if (!newMessageDetected)
        {
            _logger.LogWarning("No new message appeared within {Timeout}s timeout.", timeout / 1000);
            await CaptureScreenshotAsync(page, "teams-no-response");
            return null;
        }

        // Phase 2: Wait for agent to finish responding.
        // The agent may send a placeholder first (e.g. "Got it...working on it"),
        // then replace or follow up with the actual response. We need to wait for:
        // (a) typing indicator to clear, (b) text to not be a placeholder,
        // (c) text to stabilize.
        var previousText = string.Empty;
        var stableCount = 0;
        var phase2Start = Environment.TickCount64;
        var phase2TimeoutMs = 120_000; // 2 minutes for the real response

        while (Environment.TickCount64 - phase2Start < phase2TimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check typing indicator
            var isTyping = false;
            try
            {
                var typingIndicator = page.Locator("[data-tid='chat-typing-indicator']")
                    .Or(page.Locator(".typing-indicator"));
                isTyping = await typingIndicator.First.IsVisibleAsync().ConfigureAwait(false);
            }
            catch
            {
                // No typing indicator found
            }

            if (isTyping)
            {
                _logger.LogDebug("Agent still typing...");
                stableCount = 0;
                await page.WaitForTimeoutAsync(2000);
                continue;
            }

            var currentText = await ExtractLatestMessageTextAsync(page);

            // Check if current text looks like a placeholder
            if (IsPlaceholderResponse(currentText))
            {
                var elapsed = (Environment.TickCount64 - phase2Start) / 1000;
                _logger.LogDebug("Detected placeholder response, waiting for real response... ({Elapsed}s)", elapsed);
                stableCount = 0;
                await page.WaitForTimeoutAsync(3000);
                continue;
            }

            // Check if text has stabilized (same non-placeholder text 3 times in a row)
            if (!string.IsNullOrEmpty(currentText) && currentText == previousText)
            {
                stableCount++;
                if (stableCount >= 3)
                {
                    _logger.LogDebug("Response text stabilized ({Length} chars).", currentText.Length);
                    return currentText;
                }
            }
            else
            {
                stableCount = 0;
            }

            previousText = currentText;
            await page.WaitForTimeoutAsync(2000);
        }

        // Return whatever we have after timeout
        if (!string.IsNullOrEmpty(previousText) && !IsPlaceholderResponse(previousText))
        {
            return previousText;
        }

        _logger.LogWarning("Agent response did not stabilize within timeout.");
        await CaptureScreenshotAsync(page, "teams-response-timeout");
        return null;
    }

    /// <summary>
    /// Checks if a response looks like a short placeholder/acknowledgement
    /// that the agent sends before the real answer.
    /// </summary>
    private static bool IsPlaceholderResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // Very short responses (under 30 chars) that match placeholder patterns
        var trimmed = text.Trim();
        if (trimmed.Length > 80)
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        return PlaceholderPatterns.Any(p => lower.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Extracts the text content from the last message element in the chat pane.
    /// </summary>
    private static async Task<string> ExtractLatestMessageTextAsync(IPage page)
    {
        var selectors = new[]
        {
            "[data-tid='chat-pane-message']",
            "[data-tid='messageListItem']",
            ".message-body-content",
        };

        foreach (var selector in selectors)
        {
            var messages = page.Locator(selector);
            var count = await messages.CountAsync();
            if (count == 0)
            {
                continue;
            }

            // Get the last message's text
            try
            {
                var text = await messages.Nth(count - 1).InnerTextAsync() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
            catch
            {
                continue;
            }
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
