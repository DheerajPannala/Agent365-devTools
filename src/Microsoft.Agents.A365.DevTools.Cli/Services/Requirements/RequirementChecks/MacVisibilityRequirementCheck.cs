// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services.Helpers;
using Microsoft.Agents.A365.DevTools.Cli.Services.Internal;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that MAC visibility metrics increase after conversation simulation by
/// calling the getAgentMetrics MCP tool before and after conversation.
/// </summary>
public sealed class MacVisibilityRequirementCheck : RequirementCheck
{
    public const string BaselineFileName = "a365.metrics.baseline.json";
    public const string GetAgentMetricsToolName = "getAgentMetrics";
    public const string ObservabilityMcpServerName = "observability-mcp";

    private readonly AuthenticationService? _authService;
    private readonly string _baselineFilePath;
    private readonly bool _conversationStepVerified;
    private readonly string _environment;
    private readonly string? _baseUrlOverride;
    private readonly string? _tenantIdOverride;
    private readonly string? _agentNameOverride;
    private readonly HttpMessageHandler? _httpHandler;
    private readonly Func<CancellationToken, Task<string?>>? _tokenProviderOverride;

    public MacVisibilityRequirementCheck(
        AuthenticationService? authService,
        string baselineFilePath,
        bool conversationStepVerified,
        string environment = "prod",
        string? baseUrlOverride = null,
        string? tenantIdOverride = null,
        string? agentNameOverride = null,
        HttpMessageHandler? httpHandler = null,
        Func<CancellationToken, Task<string?>>? tokenProviderOverride = null)
    {
        _authService = authService;
        _baselineFilePath = baselineFilePath;
        _conversationStepVerified = conversationStepVerified;
        _environment = environment;
        _baseUrlOverride = baseUrlOverride;
        _tenantIdOverride = tenantIdOverride;
        _agentNameOverride = agentNameOverride;
        _httpHandler = httpHandler;
        _tokenProviderOverride = tokenProviderOverride;
    }

    /// <inheritdoc />
    public override string Name => "Visible in MAC";

    /// <inheritdoc />
    public override string Description => "Validates MAC visibility by comparing getAgentMetrics before and after conversation";

    /// <inheritdoc />
    public override string Category => "Observability";

    /// <summary>
    /// Captures pre-conversation metrics and persists them to a baseline file.
    /// </summary>
    public static async Task<RequirementCheckResult> CaptureInitialMetricsAsync(
        Agent365Config config,
        ILogger logger,
        AuthenticationService? authService,
        string environment = "prod",
        string? baseUrlOverride = null,
        string? tenantIdOverride = null,
        string? agentNameOverride = null,
        string? baselineFilePath = null,
        HttpMessageHandler? httpHandler = null,
        Func<CancellationToken, Task<string?>>? tokenProviderOverride = null,
        CancellationToken cancellationToken = default)
    {
        var filePath = string.IsNullOrWhiteSpace(baselineFilePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), BaselineFileName)
            : baselineFilePath;

        var check = new MacVisibilityRequirementCheck(
            authService,
            filePath,
            conversationStepVerified: true,
            environment,
            baseUrlOverride,
            tenantIdOverride,
            agentNameOverride,
            httpHandler,
            tokenProviderOverride);

        try
        {
            var endpoint = check.ResolveEndpoint(config);
            var metricArgument = check.ResolveMetricsArgument(config);
            var toolText = await check.CallGetAgentMetricsToolAsync(config, endpoint, metricArgument, logger, cancellationToken);
            var metrics = ParseNumericMetrics(toolText);

            if (metrics.Count == 0)
            {
                return RequirementCheckResult.Failure(
                    "Could not parse any numeric values from getAgentMetrics output",
                    "Ensure getAgentMetrics returns KPI and/or daily-series numeric values.",
                    details: "No numeric metrics were found in the MCP response text.");
            }

            var snapshot = new MacMetricsSnapshotFile
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Endpoint = endpoint,
                ToolName = GetAgentMetricsToolName,
                ServerName = ObservabilityMcpServerName,
                NumericMetrics = metrics
            };

            await check.WriteBaselineAsync(snapshot, logger, cancellationToken);

            return new RequirementCheckResult
            {
                Passed = true,
                IsWarning = false,
                Details = $"Captured initial MAC metrics to {filePath}",
                Metadata = new RequirementCheckMetadata
                {
                    MacMetricsBaselineFile = filePath,
                    MacBaselineMetrics = metrics,
                    ConversationStepVerified = true
                }
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to capture initial MAC metrics");
            return RequirementCheckResult.Failure(
                "Failed to capture initial getAgentMetrics baseline",
                "Check observability MCP endpoint settings and authentication, then retry validation.",
                details: ex.Message);
        }
    }

    /// <inheritdoc />
    public override async Task<RequirementCheckResult> CheckAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCheckWithLoggingAsync(config, logger, CheckImplementationAsync, cancellationToken);
    }

    private async Task<RequirementCheckResult> CheckImplementationAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!_conversationStepVerified)
        {
            return RequirementCheckResult.Failure(
                "Conversation simulation step is not verified as complete",
                "Ensure the designated teammate completes the Playwright mock conversation successfully before running MAC comparison.",
                details: "TODO: artifact-level teammate verification signal will be added later; current check requires conversation tier success.");
        }

        if (string.IsNullOrWhiteSpace(_baselineFilePath) || !File.Exists(_baselineFilePath))
        {
            return RequirementCheckResult.Failure(
                "Initial MAC metrics baseline file not found",
                "Run initial getAgentMetrics capture before post-conversation comparison.",
                details: $"Baseline file path: {_baselineFilePath}");
        }

        var baseline = await ReadBaselineAsync(logger, cancellationToken);
        if (baseline?.NumericMetrics is null || baseline.NumericMetrics.Count == 0)
        {
            return RequirementCheckResult.Failure(
                "Initial MAC metrics baseline is empty or invalid",
                "Regenerate the baseline and rerun validation.",
                details: $"Baseline file path: {_baselineFilePath}");
        }

        var endpoint = ResolveEndpoint(config);
        var metricArgument = ResolveMetricsArgument(config);
        var toolText = await CallGetAgentMetricsToolAsync(config, endpoint, metricArgument, logger, cancellationToken);
        var currentMetrics = ParseNumericMetrics(toolText);

        if (currentMetrics.Count == 0)
        {
            return RequirementCheckResult.Failure(
                "Could not parse any numeric values from post-conversation getAgentMetrics output",
                "Ensure getAgentMetrics output includes numeric KPI values.");
        }

        var comparisons = CompareMetrics(baseline.NumericMetrics, currentMetrics);
        var blockingFailures = comparisons
            .Where(c => !c.Passed)
            .Select(c => c.MetricKey)
            .ToList();

        var details = $"Compared {comparisons.Count} KPI metrics (exception rate excluded from increase requirement).";

        if (blockingFailures.Count > 0)
        {
            return new RequirementCheckResult
            {
                Passed = false,
                IsWarning = false,
                ErrorMessage = $"MAC metrics did not increase for required fields: {string.Join(", ", blockingFailures)}",
                ResolutionGuidance = "Execute the conversation simulation successfully and rerun validation.",
                Details = details,
                Metadata = new RequirementCheckMetadata
                {
                    MacMetricsBaselineFile = _baselineFilePath,
                    MacBaselineMetrics = baseline.NumericMetrics,
                    MacCurrentMetrics = currentMetrics,
                    MacMetricComparisons = comparisons,
                    ConversationStepVerified = _conversationStepVerified
                }
            };
        }

        return new RequirementCheckResult
        {
            Passed = true,
            IsWarning = false,
            Details = details,
            Metadata = new RequirementCheckMetadata
            {
                MacMetricsBaselineFile = _baselineFilePath,
                MacBaselineMetrics = baseline.NumericMetrics,
                MacCurrentMetrics = currentMetrics,
                MacMetricComparisons = comparisons,
                ConversationStepVerified = _conversationStepVerified
            }
        };
    }

    private string ResolveEndpoint(Agent365Config config)
    {
        var baseUrl = FirstNonEmpty(
            _baseUrlOverride,
            config.Agent365ObservabilityMcpOptions?.BaseUrl,
            Environment.GetEnvironmentVariable("A365_OBSERVABILITY_BASE_URL"))
            ?? new Uri(ConfigConstants.GetDiscoverEndpointUrl(_environment)).GetLeftPart(UriPartial.Authority);

        var tenantId = FirstNonEmpty(
            _tenantIdOverride,
            config.Agent365ObservabilityMcpOptions?.TenantId,
            Environment.GetEnvironmentVariable("A365_OBSERVABILITY_TENANT_ID"),
            config.TenantId);

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Tenant ID is required for observability MCP endpoint resolution.");
        }

        return $"{baseUrl.TrimEnd('/')}/observability/tenants/{Uri.EscapeDataString(tenantId)}/mcp";
    }

    private KeyValuePair<string, string> ResolveMetricsArgument(Agent365Config config)
    {
        var agentObservabilityId = FirstNonEmpty(
            config.Agent365ObservabilityMcpOptions?.AgentObservabilityId,
            Environment.GetEnvironmentVariable("A365_OBSERVABILITY_AGENT_OBSERVABILITY_ID"));

        if (!string.IsNullOrWhiteSpace(agentObservabilityId))
        {
            return new KeyValuePair<string, string>("agentObservabilityId", agentObservabilityId);
        }

        var agentName = FirstNonEmpty(
            _agentNameOverride,
            config.Agent365ObservabilityMcpOptions?.AgentName,
            Environment.GetEnvironmentVariable("A365_OBSERVABILITY_AGENT_NAME"),
            config.AgentIdentityDisplayName,
            config.AgentBlueprintDisplayName);

        if (string.IsNullOrWhiteSpace(agentName))
        {
            throw new InvalidOperationException(
                "Agent selector is required for getAgentMetrics. Configure agent365ObservabilityMcpOptions.agentObservabilityId or agent365ObservabilityMcpOptions.agentName, set A365_OBSERVABILITY_AGENT_OBSERVABILITY_ID / A365_OBSERVABILITY_AGENT_NAME, or configure agentIdentityDisplayName.");
        }

        return new KeyValuePair<string, string>("agentName", agentName);
    }

    private async Task<string> ResolveAccessTokenAsync(Agent365Config config, CancellationToken cancellationToken)
    {
        if (_tokenProviderOverride is not null)
        {
            var overridden = await _tokenProviderOverride(cancellationToken);
            if (string.IsNullOrWhiteSpace(overridden))
            {
                throw new InvalidOperationException("Overridden token provider returned an empty token.");
            }

            return overridden;
        }

        var envToken = Environment.GetEnvironmentVariable("A365_OBSERVABILITY_MCP_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            return envToken;
        }

        if (_authService is null)
        {
            throw new InvalidOperationException(
                "Authentication service is required when no explicit observability bearer token is configured.");
        }

        var audience = FirstNonEmpty(
            Environment.GetEnvironmentVariable("A365_OBSERVABILITY_MCP_APP_ID"),
            config.Agent365ObservabilityMcpOptions?.AppId)
            ?? ConfigConstants.GetAgent365ToolsResourceAppId(_environment);
        var loginHint = await AzCliHelper.ResolveLoginHintAsync();
        var token = await _authService.GetAccessTokenAsync(audience, userId: loginHint, ct: cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Failed to acquire access token for observability MCP call.");
        }

        return token;
    }

    private async Task<string> CallGetAgentMetricsToolAsync(
        Agent365Config config,
        string endpoint,
        KeyValuePair<string, string> metricArgument,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var token = await ResolveAccessTokenAsync(config, cancellationToken);
        var correlationId = HttpClientFactory.GenerateCorrelationId();

        using var httpClient = HttpClientFactory.CreateAuthenticatedClient(
            token,
            correlationId: correlationId,
            handler: _httpHandler);

        var toolIsAdvertised = await ProbeToolsListAsync(httpClient, endpoint, correlationId, logger, cancellationToken);
        if (!toolIsAdvertised)
        {
            throw new InvalidOperationException(
                $"MCP tools/list did not advertise required tool '{GetAgentMetricsToolName}'.");
        }

        var requestPayload = new
        {
            jsonrpc = McpConstants.JsonRpcVersion,
            id = "1",
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
            throw new InvalidOperationException(
                $"getAgentMetrics call failed with status {(int)response.StatusCode}: {content}");
        }

        return ExtractToolText(content);
    }

    private static async Task<bool> ProbeToolsListAsync(
        HttpClient httpClient,
        string endpoint,
        string correlationId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestPayload = new
            {
                jsonrpc = McpConstants.JsonRpcVersion,
                id = "tools-list",
                method = McpConstants.ToolsListMethod
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
                    "MCP tools/list probe failed with status {StatusCode} from {Endpoint} (CorrelationId: {CorrelationId})",
                    (int)response.StatusCode,
                    endpoint,
                    correlationId);
                return false;
            }

            var advertised = IsToolAdvertisedInToolsListResponse(content, GetAgentMetricsToolName);
            logger.LogInformation(
                "MCP tools/list advertised {ToolName}: {Advertised}",
                GetAgentMetricsToolName,
                advertised);
            return advertised;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "MCP tools/list probe failed unexpectedly.");
            return false;
        }
    }

    internal static bool IsToolAdvertisedInToolsListResponse(string responseContent, string toolName)
    {
        if (string.IsNullOrWhiteSpace(responseContent) || string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        try
        {
            var dataJson = string.Concat(
                responseContent
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    .Select(line => line.Substring(5).Trim()));

            var candidate = string.IsNullOrWhiteSpace(dataJson) ? responseContent : dataJson;
            var root = JsonNode.Parse(candidate);

            // MCP canonical shape: result.tools[].name
            var toolNodes = root?["result"]?["tools"]?.AsArray();
            if (toolNodes is not null)
            {
                return toolNodes.Any(n =>
                    string.Equals(
                        n?["name"]?.GetValue<string>(),
                        toolName,
                        StringComparison.OrdinalIgnoreCase));
            }

            // Fallback shape used by some servers: result.content[].text containing JSON.
            var textPayload = root?["result"]?["content"]?
                .AsArray()
                .Select(n => n?["text"]?.GetValue<string>())
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (!string.IsNullOrWhiteSpace(textPayload))
            {
                var inner = JsonNode.Parse(textPayload);
                var innerTools = inner?["tools"]?.AsArray();
                if (innerTools is not null)
                {
                    return innerTools.Any(n =>
                        string.Equals(
                            n?["name"]?.GetValue<string>(),
                            toolName,
                            StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch
        {
            // Best-effort probe; caller will proceed with tools/call.
        }

        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private async Task WriteBaselineAsync(
        MacMetricsSnapshotFile snapshot,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(_baselineFilePath, json, cancellationToken);
        logger.LogInformation("Wrote MAC baseline metrics to {Path}", _baselineFilePath);
    }

    private async Task<MacMetricsSnapshotFile?> ReadBaselineAsync(ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
            return JsonSerializer.Deserialize<MacMetricsSnapshotFile>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read baseline file {Path}", _baselineFilePath);
            return null;
        }
    }

    internal static string ExtractToolText(string responseContent)
    {
        // Handle SSE envelopes first.
        var candidate = ExtractJsonRpcCandidate(responseContent);

        try
        {
            var root = JsonNode.Parse(candidate);
            var text = root?["result"]?["content"]?
                .AsArray()
                .Select(n => n?["text"]?.GetValue<string>())
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        catch
        {
            // If candidate is already plain text/markdown, return as-is below.
        }

        return candidate;
    }

    private static string ExtractJsonRpcCandidate(string responseContent)
    {
        var dataJson = string.Concat(
            responseContent
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Substring(5).Trim()));

        return string.IsNullOrWhiteSpace(dataJson) ? responseContent : dataJson;
    }

    internal static Dictionary<string, double> ParseNumericMetrics(string toolText)
    {
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var lines = toolText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        ParseKpiTable(lines, metrics);
        ParseDailySeriesTable(lines, metrics);

        return metrics;
    }

    internal static List<MacMetricComparisonMetadata> CompareMetrics(
        IReadOnlyDictionary<string, double> baseline,
        IReadOnlyDictionary<string, double> current)
    {
        var comparisons = new List<MacMetricComparisonMetadata>();

        foreach (var key in baseline.Keys.Where(IsRelevantComparisonMetric))
        {
            if (!current.TryGetValue(key, out var currentValue))
            {
                comparisons.Add(new MacMetricComparisonMetadata
                {
                    MetricKey = key,
                    Before = baseline[key],
                    After = double.NaN,
                    Delta = double.NaN,
                    Increased = false,
                    IsExceptionRate = key.Contains("exception_rate", StringComparison.OrdinalIgnoreCase),
                    Passed = false,
                    Reason = "metric missing in post-conversation snapshot"
                });
                continue;
            }

            var before = baseline[key];
            var delta = currentValue - before;
            var increased = delta > 0;
            var isExceptionRate = key.Contains("exception_rate", StringComparison.OrdinalIgnoreCase);

            comparisons.Add(new MacMetricComparisonMetadata
            {
                MetricKey = key,
                Before = before,
                After = currentValue,
                Delta = delta,
                Increased = increased,
                IsExceptionRate = isExceptionRate,
                Passed = isExceptionRate || increased,
                Reason = isExceptionRate
                    ? "exception rate does not need to increase"
                    : (increased ? "increased" : "did not increase")
            });
        }

        return comparisons;
    }

    private static bool IsRelevantComparisonMetric(string key)
    {
        if (!key.StartsWith("kpi.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return key.EndsWith(".rl7", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith(".rl30", StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseKpiTable(List<string> lines, IDictionary<string, double> metrics)
    {
        var headerIndex = lines.FindIndex(l =>
            l.Contains('|')
            && l.Contains("Metric", StringComparison.OrdinalIgnoreCase)
            && l.Contains("RL7", StringComparison.OrdinalIgnoreCase)
            && l.Contains("RL30", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            return;
        }

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.Contains('|'))
            {
                break;
            }

            if (line.All(c => c is '|' or '-' or ':' or ' '))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToList();

            if (cells.Count < 4)
            {
                continue;
            }

            var metricName = NormalizeMetricName(cells[0]);
            if (TryParseMetricNumber(cells[1], out var rl7))
            {
                metrics[$"kpi.{metricName}.rl7"] = rl7;
            }

            if (TryParseMetricNumber(cells[2], out var rl30))
            {
                metrics[$"kpi.{metricName}.rl30"] = rl30;
            }

            if (TryParseMetricNumber(cells[3], out var wow))
            {
                metrics[$"kpi.{metricName}.wow_change_percent"] = wow;
            }
        }
    }

    private static void ParseDailySeriesTable(List<string> lines, IDictionary<string, double> metrics)
    {
        var headerIndex = lines.FindIndex(l =>
            l.Contains('|')
            && l.Contains("Date", StringComparison.OrdinalIgnoreCase)
            && l.Contains("Users", StringComparison.OrdinalIgnoreCase)
            && l.Contains("Invocations", StringComparison.OrdinalIgnoreCase)
            && l.Contains("Sessions", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            return;
        }

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.Contains('|'))
            {
                break;
            }

            if (line.All(c => c is '|' or '-' or ':' or ' '))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToList();

            if (cells.Count < 4)
            {
                continue;
            }

            var dateKey = NormalizeMetricName(cells[0]);
            if (TryParseMetricNumber(cells[1], out var users))
            {
                metrics[$"daily.{dateKey}.users"] = users;
            }

            if (TryParseMetricNumber(cells[2], out var invocations))
            {
                metrics[$"daily.{dateKey}.invocations"] = invocations;
            }

            if (TryParseMetricNumber(cells[3], out var sessions))
            {
                metrics[$"daily.{dateKey}.sessions"] = sessions;
            }
        }
    }

    internal static bool TryParseMetricNumber(string raw, out double value)
    {
        value = 0;
        var cleaned = raw.Trim();
        if (cleaned is "-" or "--")
        {
            return false;
        }

        var filtered = new string(cleaned
            .Where(c => char.IsDigit(c) || c is '.' or '-' or ',')
            .ToArray())
            .Replace(",", string.Empty, StringComparison.Ordinal);

        return double.TryParse(filtered, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeMetricName(string raw)
    {
        var input = raw.Trim().ToLowerInvariant();
        var builder = new StringBuilder(input.Length + 8);
        var pendingUnderscore = false;

        for (var i = 0; i < input.Length; i++)
        {
            if (i + 4 < input.Length && input.AsSpan(i, 5).SequenceEqual("(hrs)"))
            {
                if (builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append("hrs");
                i += 4;
                pendingUnderscore = false;
                continue;
            }

            var c = input[i];
            if (c == '%')
            {
                if (builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append("percent");
                pendingUnderscore = false;
                continue;
            }

            if (c is ' ' or '-' or '/')
            {
                pendingUnderscore = builder.Length > 0;
                continue;
            }

            if (c == '.')
            {
                continue;
            }

            if (pendingUnderscore && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(c);
            pendingUnderscore = false;
        }

        return builder.ToString().Trim('_');
    }

    private sealed class MacMetricsSnapshotFile
    {
        public DateTimeOffset CapturedAtUtc { get; set; }

        public string Endpoint { get; set; } = string.Empty;

        public string ToolName { get; set; } = string.Empty;

        public string ServerName { get; set; } = string.Empty;

        public Dictionary<string, double> NumericMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
