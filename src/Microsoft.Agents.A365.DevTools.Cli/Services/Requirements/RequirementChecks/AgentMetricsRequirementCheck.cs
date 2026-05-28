// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Services.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that agent metrics are visible and incrementing by:
/// 1. Querying baseline metrics via MCP tool call
/// 2. Generating a conversation with the agent in Copilot Chat (via Playwright)
/// 3. Re-querying metrics to verify they incremented
/// </summary>
public class AgentMetricsRequirementCheck : RequirementCheck
{
    private const string GetAgentMetricsToolName = "getAgentMetrics";

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
    /// </summary>
    protected internal virtual Task<AgentMetricsSnapshot?> GetAgentMetricsAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        return GetAgentMetricsInternalAsync(config, logger, cancellationToken);
    }

    private static async Task<AgentMetricsSnapshot?> GetAgentMetricsInternalAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable("A365_OBSERVABILITY_MCP_BEARER_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Observability MCP bearer token is not set. Configure A365_OBSERVABILITY_MCP_BEARER_TOKEN.");
            return null;
        }

        var endpoint = MacVisibilityRequirementCheck.ResolveEndpointForObservability(config, config.Environment);
        var metricArgument = MacVisibilityRequirementCheck.ResolveMetricsArgumentForObservability(config);
        var correlationId = HttpClientFactory.GenerateCorrelationId();

        using var httpClient = HttpClientFactory.CreateAuthenticatedClient(token, correlationId: correlationId);

        var toolIsAdvertised = await MacVisibilityRequirementCheck.ProbeToolsListAsync(
            httpClient,
            endpoint,
            correlationId,
            logger,
            cancellationToken);
        if (!toolIsAdvertised)
        {
            logger.LogWarning("MCP tools/list did not advertise required tool '{ToolName}'.", GetAgentMetricsToolName);
            return null;
        }

        var requestPayload = new
        {
            jsonrpc = McpConstants.JsonRpcVersion,
            id = "agent-metrics",
            method = McpConstants.ToolsCallMethod,
            @params = new
            {
                name = GetAgentMetricsToolName,
                arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [metricArgument.Key] = metricArgument.Value
                }
            }
        };

        var payloadJson = JsonSerializer.Serialize(requestPayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, McpConstants.MediaTypes.ApplicationJson)
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(McpConstants.MediaTypes.ApplicationJson));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(McpConstants.MediaTypes.TextEventStream));

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "getAgentMetrics returned non-success status {StatusCode} from {Endpoint} (CorrelationId: {CorrelationId})",
                (int)response.StatusCode,
                endpoint,
                correlationId);
            return null;
        }

        var toolText = MacVisibilityRequirementCheck.ExtractToolText(content);
        var metrics = MacVisibilityRequirementCheck.ParseNumericMetrics(toolText);
        if (metrics.Count == 0)
        {
            logger.LogWarning("getAgentMetrics response did not contain numeric metrics.");
            return null;
        }

        return new AgentMetricsSnapshot
        {
            InvocationCount = ConvertToLong(SumByMetricPrefix(metrics, "kpi.invocations.")),
            ErrorCount = ConvertToLong(SumByMetricPrefix(metrics, "kpi.errors.")),
            AverageLatencyMs = GetFirstMetricValue(metrics,
                "kpi.latency.avg",
                "kpi.latency.average",
                "kpi.avgLatencyMs",
                "kpi.averageLatencyMs")
        };
    }

    private static double SumByMetricPrefix(IReadOnlyDictionary<string, double> metrics, string prefix)
    {
        double sum = 0;
        foreach (var pair in metrics)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                sum += pair.Value;
            }
        }

        return sum;
    }

    private static double? GetFirstMetricValue(IReadOnlyDictionary<string, double> metrics, params string[] metricKeys)
    {
        foreach (var metricKey in metricKeys)
        {
            if (metrics.TryGetValue(metricKey, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static long ConvertToLong(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        if (value > long.MaxValue)
        {
            return long.MaxValue;
        }

        if (value < long.MinValue)
        {
            return long.MinValue;
        }

        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
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
