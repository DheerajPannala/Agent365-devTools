// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
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
/// 1. Starting the agent locally
/// 2. Querying baseline metrics via MCP tool call
/// 3. Generating a conversation with the agent in Teams Chat (via Playwright)
/// 4. Re-querying metrics to verify they incremented
/// 5. Stopping the agent
/// </summary>
public class AgentMetricsRequirementCheck : RequirementCheck
{
    private const string GetAgentMetricsToolName = "getAgentMetrics";

    private readonly CopilotChatPlaywrightService? _playwrightService;
    private readonly string? _instanceName;
    private readonly PlatformDetector? _platformDetector;
    private readonly IProcessService? _processService;
    private readonly string? _resolvedUvCommand;

    /// <summary>Default test message sent to the agent during the metrics check.</summary>
    internal const string DefaultTestMessage = ConversationRequirementCheck.FallbackToolPrompt;

    /// <summary>Maximum time to wait for the agent to start and respond on the health endpoint.</summary>
    private static readonly TimeSpan AgentStartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Interval between health endpoint polls during startup.</summary>
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(500);

    public AgentMetricsRequirementCheck(
        CopilotChatPlaywrightService? playwrightService = null,
        string? instanceName = null,
        PlatformDetector? platformDetector = null,
        IProcessService? processService = null,
        string? resolvedUvCommand = null)
    {
        _playwrightService = playwrightService;
        _instanceName = instanceName;
        _platformDetector = platformDetector;
        _processService = processService;
        _resolvedUvCommand = resolvedUvCommand;
    }
    /// <inheritdoc />
    public override string Name => "AgentMetrics";

    /// <inheritdoc />
    public override string Description => "Verifies agent metrics are visible and incrementing after a Teams Chat conversation";

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
        Process? agentProcess = null;

        try
        {
            // Step 0: Start the agent locally
            logger.LogInformation("Starting agent locally for metrics validation...");
            agentProcess = await StartAgentLocallyAsync(config, logger, cancellationToken);
            if (agentProcess is null)
            {
                return RequirementCheckResult.Warning(
                    "Could not start the agent locally for metrics validation",
                    details: "Ensure the project builds and runs successfully. Run 'a365 validate' without --with-tenant first.");
            }

            // Step 1: Get baseline metrics via MCP tool call (best-effort, does not block step 2)
            logger.LogInformation("Step 1: Querying baseline agent metrics...");
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

            // Step 2: Generate a conversation with the agent in Teams Chat (via Playwright)
            logger.LogInformation("Step 2: Generating conversation with agent in Teams Chat...");
            bool conversationGenerated;
            try
            {
                conversationGenerated = await GenerateCopilotChatConversationAsync(config, logger, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to generate Teams Chat conversation: {Message}", ex.Message);
                return RequirementCheckResult.Warning(
                    "Could not generate Teams Chat conversation for metrics validation",
                    details: $"Playwright test failed: {ex.Message}");
            }

            metadata.ConversationGenerated = conversationGenerated;

            if (!conversationGenerated)
            {
                return RequirementCheckResult.Warning(
                    "Teams Chat conversation could not be generated",
                    details: "Playwright was unable to complete a conversation with the agent. Metrics increment check skipped.");
            }

            if (baselineMetrics is null)
            {
                return RequirementCheckResult.Failure(
                    "Agent metrics endpoint not available",
                    "Ensure the agent is deployed and metrics are configured. The MCP metrics tool must be reachable.",
                    details: "Teams Chat conversation completed successfully but baseline metrics could not be retrieved.");
            }

            // Step 3: Re-query metrics and verify they incremented
            logger.LogInformation("Step 3: Re-querying agent metrics to verify increment...");
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
                    details: "Teams Chat conversation completed successfully but post-conversation metrics could not be retrieved.");
            }

            var incremented = postConversationMetrics.InvocationCount > baselineMetrics.InvocationCount;
            metadata.MetricsIncremented = incremented;

            if (!incremented)
            {
                return RequirementCheckResult.Failure(
                    "Agent metrics did not increment after Teams Chat conversation",
                    "Verify that the agent is instrumented with Agent365 observability and that metrics are flowing to the backend.",
                    details: $"Baseline invocations: {baselineMetrics.InvocationCount}, " +
                        $"Post-conversation invocations: {postConversationMetrics.InvocationCount}");
            }

            return RequirementCheckResult.Success(
                details: $"Agent metrics incremented from {baselineMetrics.InvocationCount} to " +
                    $"{postConversationMetrics.InvocationCount} after Teams Chat conversation.");
        }
        finally
        {
            if (agentProcess is not null && !agentProcess.HasExited)
            {
                logger.LogDebug("Stopping local agent process...");
                try
                {
                    agentProcess.Kill(entireProcessTree: true);
                    await agentProcess.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch
                {
                    // Best-effort cleanup
                }
                finally
                {
                    agentProcess.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Starts the agent locally and waits for the health endpoint to respond.
    /// Returns the agent process, or null if the agent could not be started.
    /// </summary>
    protected internal virtual async Task<Process?> StartAgentLocallyAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (_platformDetector is null || _processService is null)
        {
            logger.LogWarning("Platform detector or process service not available. Cannot start agent locally.");
            return null;
        }

        var projectPath = ConversationRequirementCheck.ResolveProjectPath(config);
        if (!Directory.Exists(projectPath))
        {
            logger.LogWarning("Project path does not exist: {Path}", projectPath);
            return null;
        }

        var platform = _platformDetector.Detect(projectPath);
        if (platform == ProjectPlatform.Unknown)
        {
            logger.LogWarning("Could not detect project platform in {Path}", projectPath);
            return null;
        }

        var port = LocalRuntimeRequirementCheck.ResolvePort(config.MessagingEndpoint);
        var healthUrl = $"http://localhost:{port}{LocalRuntimeRequirementCheck.DefaultHealthPath}";

        logger.LogInformation("Starting agent locally ({Platform} on port {Port})...", platform, port);

        var startInfo = BuildProcessStartInfo(platform, projectPath, port);
        var process = _processService.Start(startInfo);
        if (process is null)
        {
            logger.LogWarning("Failed to start {Platform} process.", platform);
            return null;
        }

        var healthResult = await WaitForHealthAsync(process, healthUrl, logger, cancellationToken);
        if (!healthResult)
        {
            logger.LogWarning("Agent did not respond on health endpoint within timeout.");
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            return null;
        }

        logger.LogInformation("Agent is running and healthy on port {Port}.", port);
        return process;
    }

    private ProcessStartInfo BuildProcessStartInfo(ProjectPlatform platform, string projectPath, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = projectPath,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        switch (platform)
        {
            case ProjectPlatform.DotNet:
                startInfo.FileName = "dotnet";
                startInfo.Arguments = "run --no-build";
                startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://localhost:{port}";
                break;

            case ProjectPlatform.NodeJs:
                LocalRuntimeRequirementCheck.WrapForWindows(startInfo, "npm", "start");
                startInfo.EnvironmentVariables["PORT"] = port.ToString();
                break;

            case ProjectPlatform.Python:
                var entryPoint = LocalRuntimeRequirementCheck.ResolvePythonEntryPoint(projectPath);
                var usesUv = ProjectBuildRequirementCheck.DetectPythonInstallCommand(projectPath) is ("uv", _);
                if (usesUv)
                {
                    startInfo.FileName = _resolvedUvCommand ?? "uv";
                    startInfo.Arguments = $"run python {entryPoint}";
                }
                else
                {
                    startInfo.FileName = "python";
                    startInfo.Arguments = entryPoint;
                }
                startInfo.EnvironmentVariables["PORT"] = port.ToString();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform");
        }

        return startInfo;
    }

    private static async Task<bool> WaitForHealthAsync(
        Process process,
        string healthUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AgentStartupTimeout);

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                logger.LogWarning("Agent exited with code {ExitCode} before health endpoint responded.", process.ExitCode);
                return false;
            }

            try
            {
                using var response = await httpClient.GetAsync(healthUrl, timeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(HealthPollInterval, timeoutCts.Token);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
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
    /// Generates a conversation with the agent in Microsoft Teams using Playwright.
    /// Reuses the CLI's existing MSAL authentication context (WAM on Windows,
    /// browser auth on other platforms) so the user is not prompted to log in again.
    ///
    /// Uses <see cref="CopilotChatPlaywrightService"/> to:
    /// 1. Launch a Chromium browser (headless if saved auth state is fresh, headed otherwise)
    /// 2. Navigate to Teams web, search for the agent by name, send a test message
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

        var agentName = _instanceName;
        if (string.IsNullOrWhiteSpace(agentName))
        {
            logger.LogWarning("Instance name not provided. Use --instance-name to specify the agent name in Teams.");
            return false;
        }

        logger.LogInformation("Opening Teams chat conversation with agent '{AgentName}'...", agentName);

        var projectPath = Directory.GetCurrentDirectory();
        var testMessage = ConversationRequirementCheck.BuildToolInvocationPrompt(projectPath, logger);
        logger.LogDebug("Using test message: {Message}", testMessage);

        var response = await _playwrightService.SendMessageToAgentAsync(
            agentName,
            testMessage,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            logger.LogWarning("Agent did not respond to the test message in Teams.");
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
