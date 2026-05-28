// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.DevTools.Validation;

/// <summary>
/// Root model for the structured validation report written to a365.validate.json.
/// </summary>
public sealed class ValidateReport
{
    [JsonPropertyName("agent")]
    public AgentInfo Agent { get; set; } = new();

    [JsonPropertyName("tiers")]
    public ValidationTiers Tiers { get; set; } = new();

    [JsonPropertyName("summary")]
    public SummaryResult Summary { get; set; } = new();

    [JsonPropertyName("agentConsoleLogFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentConsoleLogFile { get; set; }
}

/// <summary>
/// Metadata about the agent project being validated.
/// </summary>
public sealed class AgentInfo
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("framework")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Framework { get; set; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Capabilities { get; set; }
}

/// <summary>
/// Container for all validation tiers.
/// </summary>
public sealed class ValidationTiers
{
    [JsonPropertyName("structural")]
    public StructuralTierResult Structural { get; set; } = TierResult.CreateSkipped<StructuralTierResult>();

    [JsonPropertyName("build")]
    public BuildTierResult Build { get; set; } = TierResult.CreateSkipped<BuildTierResult>();

    [JsonPropertyName("boot")]
    public BootTierResult Boot { get; set; } = TierResult.CreateSkipped<BootTierResult>();

    [JsonPropertyName("conversation")]
    public ConversationTierResult Conversation { get; set; } = TierResult.CreateSkipped<ConversationTierResult>("not yet implemented");

    [JsonPropertyName("telemetry")]
    public TelemetryTierResult Telemetry { get; set; } = TierResult.CreateSkipped<TelemetryTierResult>("not yet run");

    [JsonPropertyName("blueprint")]
    public BlueprintTierResult Blueprint { get; set; } = TierResult.CreateSkipped<BlueprintTierResult>("not yet implemented");

}

/// <summary>
/// Base tier result. When skipped, ok is null.
/// </summary>
public class TierResult
{
    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ok { get; set; }

    [JsonPropertyName("skipped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Skipped { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    [JsonPropertyName("warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; set; }

    public static TierResult CreateSkipped(string reason = "not yet implemented")
    {
        return new TierResult { Skipped = true, Reason = reason };
    }

    public static T CreateSkipped<T>(string reason = "not yet implemented") where T : TierResult, new()
    {
        return new T { Skipped = true, Reason = reason };
    }
}

/// <summary>
/// Structural tier: config and manifest validation checks.
/// </summary>
public sealed class StructuralTierResult : TierResult
{
    [JsonPropertyName("checks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StructuralCheck>? Checks { get; set; }
}

/// <summary>
/// Individual structural check result.
/// </summary>
public sealed class StructuralCheck
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

/// <summary>
/// Build tier: project compilation result.
/// </summary>
public sealed class BuildTierResult : TierResult
{
    [JsonPropertyName("log")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Log { get; set; }

    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; set; }

    [JsonPropertyName("errorSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorSummary { get; set; }

    [JsonPropertyName("buildLogFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuildLogFile { get; set; }
}

/// <summary>
/// Boot tier: local runtime health probe result.
/// </summary>
public sealed class BootTierResult : TierResult
{
    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Port { get; set; }

    [JsonPropertyName("bootMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BootMs { get; set; }

    [JsonPropertyName("bootLogFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BootLogFile { get; set; }
}

/// <summary>
/// Conversation tier: multi-turn conversation validation result.
/// </summary>
public sealed class ConversationTierResult : TierResult
{
    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ConversationTurnResult>? Turns { get; set; }

    [JsonPropertyName("playgroundLaunched")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PlaygroundLaunched { get; set; }

    [JsonPropertyName("conversationLogFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationLogFile { get; set; }
}

/// <summary>
/// Telemetry tier: trace export validation result.
/// </summary>
public sealed class TelemetryTierResult : TierResult
{
    [JsonPropertyName("consoleExporterActive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ConsoleExporterActive { get; set; }

    [JsonPropertyName("foundOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? FoundOperations { get; set; }

    [JsonPropertyName("missingOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingOperations { get; set; }

    [JsonPropertyName("scopeVersionPresent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ScopeVersionPresent { get; set; }

    [JsonPropertyName("parentLinksValid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ParentLinksValid { get; set; }

    [JsonPropertyName("childSpansMissingParent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ChildSpansMissingParent { get; set; }

    [JsonPropertyName("resourceAttributesPresent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResourceAttributesPresent { get; set; }

    [JsonPropertyName("missingResourceAttributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingResourceAttributes { get; set; }
}

/// <summary>
/// Blueprint tier: Entra registration, permissions, and consent validation.
/// </summary>
public sealed class BlueprintTierResult : TierResult
{
    [JsonPropertyName("appExists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AppExists { get; set; }

    [JsonPropertyName("servicePrincipalExists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ServicePrincipalExists { get; set; }

    [JsonPropertyName("registrationExists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RegistrationExists { get; set; }

    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BlueprintResourceResult>? Resources { get; set; }
}

/// <summary>
/// Permission and consent status for a single resource API in the blueprint.
/// </summary>
public sealed class BlueprintResourceResult
{
    [JsonPropertyName("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonPropertyName("resourceAppId")]
    public string ResourceAppId { get; set; } = string.Empty;

    [JsonPropertyName("expectedScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ExpectedScopes { get; set; }

    [JsonPropertyName("actualScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ActualScopes { get; set; }

    [JsonPropertyName("missingScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingScopes { get; set; }

    [JsonPropertyName("consentGranted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ConsentGranted { get; set; }

    [JsonPropertyName("inheritablePermissionsConfigured")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InheritablePermissionsConfigured { get; set; }

    [JsonPropertyName("scopesAllAllowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ScopesAllAllowed { get; set; }

    [JsonPropertyName("rolesAllAllowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RolesAllAllowed { get; set; }

    [JsonPropertyName("actualAppRoles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ActualAppRoles { get; set; }

    [JsonPropertyName("effectiveInheritance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EffectiveInheritance { get; set; }
}

/// <summary>
/// Agent metrics tier result with baseline/post-conversation snapshots and increment status.
/// </summary>
public sealed class AgentMetricsTierResult : TierResult
{
    [JsonPropertyName("baselineMetrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentMetricsSnapshotResult? BaselineMetrics { get; set; }

    [JsonPropertyName("conversationGenerated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ConversationGenerated { get; set; }

    [JsonPropertyName("postConversationMetrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentMetricsSnapshotResult? PostConversationMetrics { get; set; }

    [JsonPropertyName("metricsIncremented")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MetricsIncremented { get; set; }
}

/// <summary>
/// Snapshot of agent metrics at a point in time.
/// </summary>
public sealed class AgentMetricsSnapshotResult
{
    [JsonPropertyName("invocationCount")]
    public long InvocationCount { get; set; }

    [JsonPropertyName("errorCount")]
    public long ErrorCount { get; set; }

    [JsonPropertyName("averageLatencyMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AverageLatencyMs { get; set; }
}

/// <summary>
/// MAC visibility tier: compares getAgentMetrics snapshots before and after conversation.
/// </summary>
public sealed class MacTierResult : TierResult
{
    [JsonPropertyName("baselineFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaselineFile { get; set; }

    [JsonPropertyName("conversationVerified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ConversationVerified { get; set; }

    [JsonPropertyName("baselineMetrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double>? BaselineMetrics { get; set; }

    [JsonPropertyName("currentMetrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double>? CurrentMetrics { get; set; }

    [JsonPropertyName("comparisons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MacMetricComparisonResult>? Comparisons { get; set; }
}

/// <summary>
/// Result of comparing a single MAC metric between baseline and post-conversation snapshots.
/// </summary>
public sealed class MacMetricComparisonResult
{
    [JsonPropertyName("metricKey")]
    public string MetricKey { get; set; } = string.Empty;

    [JsonPropertyName("before")]
    public double Before { get; set; }

    [JsonPropertyName("after")]
    public double After { get; set; }

    [JsonPropertyName("delta")]
    public double Delta { get; set; }

    [JsonPropertyName("increased")]
    public bool Increased { get; set; }

    [JsonPropertyName("isExceptionRate")]
    public bool IsExceptionRate { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

/// <summary>
/// Result of a single conversation turn.
/// </summary>
public sealed class ConversationTurnResult
{
    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;

    [JsonPropertyName("statusCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StatusCode { get; set; }

    [JsonPropertyName("responseSnippet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseSnippet { get; set; }

    [JsonPropertyName("latencyMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LatencyMs { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("agentResponded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AgentResponded { get; set; }

    [JsonPropertyName("agentResponseText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentResponseText { get; set; }
}

/// <summary>
/// Summary of the validation run.
/// </summary>
public sealed class SummaryResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("blocker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blocker { get; set; }
}
