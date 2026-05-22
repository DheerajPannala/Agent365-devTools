// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that the agent is exporting telemetry traces by analyzing the agent's
/// console output log file captured during the conversation validation step.
/// </summary>
public class TelemetryRequirementCheck : RequirementCheck
{
    private readonly string? _agentConsoleLogPath;

    /// <summary>
    /// Maximum number of telemetry-relevant log lines to analyze.
    /// </summary>
    internal const int MaxTelemetryLines = 100;

    /// <summary>
    /// Keywords that identify a log line as telemetry-related.
    /// A line must contain at least one of these to be considered relevant.
    /// </summary>
    internal static readonly string[] TelemetryContextKeywords = new[]
    {
        "opentelemetry",
        "otel",
        "otlp",
        "tracer",
        "tracerprovider",
        "activitysource",
        "span",
        "exporter",
        "agent365observability",
        "agent365.observability",
        "agent365exporter",
        "otelwrite",
        "batchexportprocessor",
        "traces exported",
        "export completed",
        "export succeeded"
    };

    /// <summary>
    /// Patterns that indicate successful trace export when found in telemetry-relevant lines.
    /// </summary>
    internal static readonly string[] SuccessPatterns = new[]
    {
        "export completed",
        "export succeeded",
        "successfully exported",
        "traces exported",
        "span exported",
        "tracerprovider built",
        "tracerprovider started",
        "otlpexporter",
        "otlptraceexporter"
    };

    /// <summary>
    /// Patterns that indicate trace export failure when found in telemetry-relevant lines.
    /// </summary>
    internal static readonly string[] FailurePatterns = new[]
    {
        "export failed",
        "exporter error",
        "connection refused",
        "unavailable",
        "deadline_exceeded",
        "unauthenticated",
        "permissiondenied",
        "failed to export",
        "exporter threw",
        "dropped spans",
        "nothing exported",
        "spans skipped",
        "spans filtered out",
        "missing tenant",
        "missing agent id"
    };

    public TelemetryRequirementCheck(string? agentConsoleLogPath)
    {
        _agentConsoleLogPath = agentConsoleLogPath;
    }

    /// <inheritdoc />
    public override string Name => "Telemetry";

    /// <inheritdoc />
    public override string Description => "Validates that telemetry traces are being exported to Agent365";

    /// <inheritdoc />
    public override string Category => "Observability";

    /// <inheritdoc />
    public override async Task<RequirementCheckResult> CheckAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCheckWithLoggingAsync(config, logger, CheckImplementationAsync, cancellationToken);
    }

    private Task<RequirementCheckResult> CheckImplementationAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_agentConsoleLogPath) || !File.Exists(_agentConsoleLogPath))
        {
            return Task.FromResult(RequirementCheckResult.Warning(
                "No agent console log file available to analyze for telemetry",
                details: "Telemetry check requires agent console output from the conversation step"));
        }

        logger.LogDebug("Analyzing agent console log at {LogPath}", _agentConsoleLogPath);

        string[] logLines;
        try
        {
            logLines = File.ReadAllLines(_agentConsoleLogPath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read agent console log file");
            return Task.FromResult(RequirementCheckResult.Warning(
                "Could not read agent console log file",
                details: $"Failed to read {_agentConsoleLogPath}: {ex.Message}"));
        }

        var relevantLines = FilterTelemetryLines(logLines);

        if (relevantLines.Count == 0)
        {
            return Task.FromResult(RequirementCheckResult.Failure(
                "No telemetry-related output detected in agent console logs",
                "Configure OpenTelemetry in your agent to export traces to Agent365.",
                details: "No OpenTelemetry, OTLP, or trace export evidence found in agent console output."));
        }

        logger.LogDebug("Found {Count} telemetry-relevant log lines", relevantLines.Count);

        var matchedSuccessPatterns = FindMatchingPatterns(relevantLines, SuccessPatterns);
        var matchedFailurePatterns = FindMatchingPatterns(relevantLines, FailurePatterns);

        // Failure takes precedence over success
        if (matchedFailurePatterns.Count > 0)
        {
            var failureDetails = string.Join(", ", matchedFailurePatterns);
            var guidance = GetFailureGuidance(matchedFailurePatterns);
            return Task.FromResult(RequirementCheckResult.Failure(
                $"Telemetry export failures detected: {failureDetails}",
                guidance,
                details: $"Failure patterns found: {failureDetails}. " +
                    $"Success patterns found: {(matchedSuccessPatterns.Count > 0 ? string.Join(", ", matchedSuccessPatterns) : "none")}. " +
                    $"Analyzed {relevantLines.Count} telemetry-relevant log lines."));
        }

        if (matchedSuccessPatterns.Count > 0)
        {
            var successDetails = string.Join(", ", matchedSuccessPatterns);
            return Task.FromResult(RequirementCheckResult.Success(
                details: $"Telemetry export evidence found: {successDetails}. " +
                    $"Analyzed {relevantLines.Count} telemetry-relevant log lines."));
        }

        // Telemetry lines exist but no clear success or failure — treat as failure
        return Task.FromResult(RequirementCheckResult.Failure(
            "Telemetry SDK detected but no trace export evidence found",
            "Ensure traces are being exported to the Agent365 OTLP endpoint.",
            details: $"Found {relevantLines.Count} telemetry-related log lines but could not " +
                "confirm successful trace export."));
    }

    /// <summary>
    /// Filters log lines to only those containing telemetry-related keywords.
    /// </summary>
    internal static List<string> FilterTelemetryLines(string[] logLines)
    {
        var result = new List<string>();
        foreach (var line in logLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var lower = line.ToLowerInvariant();
            foreach (var keyword in TelemetryContextKeywords)
            {
                if (lower.Contains(keyword))
                {
                    result.Add(line);
                    if (result.Count >= MaxTelemetryLines)
                        return result;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds which patterns from the given set appear in the relevant log lines.
    /// </summary>
    internal static List<string> FindMatchingPatterns(List<string> relevantLines, string[] patterns)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in relevantLines)
        {
            var lower = line.ToLowerInvariant();
            foreach (var pattern in patterns)
            {
                if (lower.Contains(pattern) && matched.Add(pattern))
                {
                    // Found a new pattern match
                }
            }
        }

        return matched.ToList();
    }

    private static string GetFailureGuidance(List<string> failurePatterns)
    {
        var lower = failurePatterns.Select(p => p.ToLowerInvariant()).ToHashSet();

        if (lower.Contains("missing tenant") || lower.Contains("missing agent id") ||
            lower.Contains("nothing exported") || lower.Contains("spans skipped"))
            return "Configure tenant ID and agent ID in your agent's observability settings. " +
                "The Agent365 exporter requires both to export spans.";

        if (lower.Contains("connection refused") || lower.Contains("unavailable") || lower.Contains("deadline_exceeded"))
            return "Check that the OTLP endpoint is reachable from the agent. " +
                "Verify the endpoint URL and network connectivity.";

        if (lower.Contains("unauthenticated") || lower.Contains("permissiondenied"))
            return "Check OTLP endpoint credentials. " +
                "Ensure the agent has valid authentication for the observability endpoint.";

        return "Check the agent console logs for telemetry export error details.";
    }
}
