// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that agent metrics are visible and incrementing by:
/// 1. Querying baseline metrics via MCP tool call
/// 2. Generating a conversation with the agent in Copilot Chat (via Playwright)
/// 3. Re-querying metrics to verify they incremented
/// </summary>
public class AgentMetricsRequirementCheck : RequirementCheck
{
    private readonly CopilotChatPlaywrightService? _playwrightService;
    private readonly string? _instanceName;

    /// <summary>Default test message sent to the agent during the metrics check.</summary>
    internal const string DefaultTestMessage = ConversationRequirementCheck.FallbackToolPrompt;

    public AgentMetricsRequirementCheck(
        CopilotChatPlaywrightService? playwrightService = null,
        string? instanceName = null)
    {
        _playwrightService = playwrightService;
        _instanceName = instanceName;
    }
    /// <inheritdoc />
    public override string Name => "AgentMetrics";

    /// <inheritdoc />
    public override string Description => "Verifies agent metrics are visible and incrementing after a Copilot Chat conversation";

    /// <inheritdoc />
    public override string Category => "Observability";

    /// <inheritdoc />
    public override Task<RequirementCheckResult> CheckAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCheckWithLoggingAsync(config, logger, CheckImplementationAsync, cancellationToken);
    }

    private async Task<RequirementCheckResult> CheckImplementationAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var metadata = new AgentMetricsMetadata();

        // Step 1: Get baseline metrics via MCP tool call (best-effort, does not block step 2)
        logger.LogDebug("Step 1: Querying baseline agent metrics...");
        AgentMetricsSnapshot? baselineMetrics = null;
        try
        {
            baselineMetrics = await GetAgentMetricsAsync(config, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to query baseline agent metrics: {Message}", ex.Message);
        }

        metadata.BaselineMetrics = baselineMetrics;
        if (baselineMetrics is null)
        {
            logger.LogDebug("Baseline metrics not available -- will still attempt conversation.");
        }

        // Step 2: Generate a conversation with the agent in Copilot Chat (via Playwright)
        logger.LogDebug("Step 2: Generating conversation with agent in Copilot Chat...");
        bool conversationGenerated;
        try
        {
            conversationGenerated = await GenerateCopilotChatConversationAsync(config, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to generate Copilot Chat conversation");
            return RequirementCheckResult.Warning(
                "Could not generate Copilot Chat conversation for metrics validation",
                details: $"Playwright test failed: {ex.Message}");
        }

        metadata.ConversationGenerated = conversationGenerated;

        if (!conversationGenerated)
        {
            return RequirementCheckResult.Warning(
                "Copilot Chat conversation could not be generated",
                details: "Playwright was unable to complete a conversation with the agent. Metrics increment check skipped.");
        }

        // If baseline metrics were not available, fail — metrics must be reachable
        if (baselineMetrics is null)
        {
            return RequirementCheckResult.Failure(
                "Agent metrics endpoint not available",
                "Ensure the agent is deployed and metrics are configured. The MCP metrics tool must be reachable.",
                details: "Copilot Chat conversation completed successfully but baseline metrics could not be retrieved.");
        }

        // Step 3: Re-query metrics and verify they incremented
        logger.LogDebug("Step 3: Re-querying agent metrics to verify increment...");
        AgentMetricsSnapshot? postConversationMetrics = null;
        try
        {
            postConversationMetrics = await GetAgentMetricsAsync(config, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to query post-conversation agent metrics: {Message}", ex.Message);
        }

        metadata.PostConversationMetrics = postConversationMetrics;

        if (postConversationMetrics is null)
        {
            return RequirementCheckResult.Failure(
                "Agent metrics endpoint not available after conversation",
                "Ensure the metrics endpoint remains reachable. The MCP metrics tool must return data.",
                details: "Copilot Chat conversation completed successfully but post-conversation metrics could not be retrieved.");
        }

        // Compare baseline vs post-conversation
        var incremented = postConversationMetrics.InvocationCount > baselineMetrics.InvocationCount;
        metadata.MetricsIncremented = incremented;

        if (!incremented)
        {
            return RequirementCheckResult.Failure(
                "Agent metrics did not increment after Copilot Chat conversation",
                "Verify that the agent is instrumented with Agent365 observability and that metrics are flowing to the backend.",
                details: $"Baseline invocations: {baselineMetrics.InvocationCount}, " +
                    $"Post-conversation invocations: {postConversationMetrics.InvocationCount}");
        }

        return RequirementCheckResult.Success(
            details: $"Agent metrics incremented from {baselineMetrics.InvocationCount} to " +
                $"{postConversationMetrics.InvocationCount} after Copilot Chat conversation.");
    }

    /// <summary>
    /// Queries agent metrics via MCP tool call.
    /// This is a placeholder — the actual implementation will call an MCP server tool
    /// to retrieve agent telemetry/metrics from the observability backend.
    /// </summary>
    protected internal virtual Task<AgentMetricsSnapshot?> GetAgentMetricsAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual MCP tool call to retrieve agent metrics
        // Expected call: invoke MCP tool "get_agent_metrics" with agent app ID
        // Returns: invocation count, error count, latency percentiles, etc.
        logger.LogDebug("Agent metrics MCP tool call not yet implemented — returning null placeholder");
        return Task.FromResult<AgentMetricsSnapshot?>(null);
    }

    /// <summary>
    /// Generates a conversation with the agent in Copilot Chat using Playwright.
    /// Reuses the CLI's existing MSAL authentication context (WAM on Windows,
    /// browser auth on other platforms) so the user is not prompted to log in again.
    ///
    /// Uses <see cref="CopilotChatPlaywrightService"/> to:
    /// 1. Launch a Chromium browser (headless if saved auth state is fresh, headed otherwise)
    /// 2. Navigate to M365 Chat, select the agent, send a test message
    /// 3. Wait for the agent to respond
    /// 4. Save browser auth state for future runs
    /// </summary>
    protected internal virtual async Task<bool> GenerateCopilotChatConversationAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (_playwrightService is null)
        {
            logger.LogDebug("CopilotChatPlaywrightService not provided -- returning false placeholder.");
            return false;
        }

        // Instance name is required (provided via --instance-name)
        var agentName = _instanceName;
        if (string.IsNullOrWhiteSpace(agentName))
        {
            logger.LogWarning("Instance name not provided. Use --instance-name to specify the agent name in Copilot Chat.");
            return false;
        }

        logger.LogInformation("Opening Copilot Chat conversation with agent '{AgentName}'...", agentName);

        // Use the same tool-specific prompt as the conversation check
        var projectPath = Directory.GetCurrentDirectory();
        var testMessage = ConversationRequirementCheck.BuildToolInvocationPrompt(projectPath, logger);
        logger.LogDebug("Using test message: {Message}", testMessage);

        var response = await _playwrightService.SendMessageToAgentAsync(
            agentName,
            testMessage,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            logger.LogWarning("Agent did not respond to the test message.");
            return false;
        }

        logger.LogInformation("Agent responded successfully ({Length} chars).", response.Length);
        return true;
    }
}

/// <summary>
/// Snapshot of agent metrics at a point in time.
/// </summary>
public class AgentMetricsSnapshot
{
    /// <summary>Total number of agent invocations recorded.</summary>
    public long InvocationCount { get; set; }

    /// <summary>Total number of errors recorded.</summary>
    public long ErrorCount { get; set; }

    /// <summary>Average latency in milliseconds (if available).</summary>
    public double? AverageLatencyMs { get; set; }
}

/// <summary>
/// Metadata for agent metrics check results, used for structured report output.
/// </summary>
public class AgentMetricsMetadata
{
    /// <summary>Metrics snapshot before the conversation.</summary>
    public AgentMetricsSnapshot? BaselineMetrics { get; set; }

    /// <summary>Whether a Copilot Chat conversation was successfully generated.</summary>
    public bool? ConversationGenerated { get; set; }

    /// <summary>Metrics snapshot after the conversation.</summary>
    public AgentMetricsSnapshot? PostConversationMetrics { get; set; }

    /// <summary>Whether metrics incremented after the conversation.</summary>
    public bool? MetricsIncremented { get; set; }
}
