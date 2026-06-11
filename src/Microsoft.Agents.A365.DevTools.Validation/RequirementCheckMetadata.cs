// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.DevTools.Validation;

/// <summary>
/// Typed metadata for structured validation report output.
/// Attached to RequirementCheckResult.Metadata by validate-specific checks.
/// </summary>
public sealed class RequirementCheckMetadata
{
    /// <summary>Port the app is running on (boot tier).</summary>
    public int? Port { get; init; }

    /// <summary>Time in milliseconds for the app to respond (boot tier).</summary>
    public long? BootMs { get; init; }

    /// <summary>Build or runtime log output (build/boot tier).</summary>
    public string? Log { get; init; }

    /// <summary>Process exit code (build tier).</summary>
    public int? ExitCode { get; init; }

    /// <summary>Detected platform name (build/boot tier).</summary>
    public string? Platform { get; init; }

    /// <summary>Conversation turn results (conversation tier).</summary>
    public List<ConversationTurnMetadata>? Turns { get; init; }

    /// <summary>Whether AgentsPlayground was launched for interactive testing.</summary>
    public bool? PlaygroundLaunched { get; init; }

    /// <summary>
    /// Path to the agent's captured console output log file.
    /// Written during the conversation step; used by telemetry check and referenced in the report.
    /// </summary>
    public string? AgentConsoleLogPath { get; init; }

    /// <summary>
    /// Path to the MSBuild file log written during project build validation.
    /// </summary>
    public string? BuildLogFile { get; init; }

    /// <summary>
    /// Path to the boot log file written during local runtime validation.
    /// </summary>
    public string? BootLogFile { get; init; }

    /// <summary>
    /// Path to the conversation log file written during conversation validation.
    /// Contains HTTP request/response details for each turn.
    /// </summary>
    public string? ConversationLogFile { get; init; }

    /// <summary>
    /// Resolved path to the uv command, set during build dependency install.
    /// Used by the boot step to run Python agents in uv-managed projects.
    /// </summary>
    public string? ResolvedUvCommand { get; init; }

    /// <summary>Whether the blueprint application exists in Entra ID.</summary>
    public bool? AppExists { get; init; }

    /// <summary>Whether a service principal exists for the blueprint.</summary>
    public bool? ServicePrincipalExists { get; init; }

    /// <summary>Whether the agent registration exists (null if not configured).</summary>
    public bool? RegistrationExists { get; init; }

    /// <summary>Resource permission results from comparing config vs Entra.</summary>
    public List<BlueprintResourcePermission>? ResourcePermissions { get; set; }

    /// <summary>
    /// Path to the persisted pre-conversation MAC metrics baseline file.
    /// </summary>
    public string? MacMetricsBaselineFile { get; init; }

    /// <summary>
    /// Flattened numeric metrics captured before conversation.
    /// </summary>
    public Dictionary<string, double>? MacBaselineMetrics { get; init; }

    /// <summary>
    /// Flattened numeric metrics captured after conversation.
    /// </summary>
    public Dictionary<string, double>? MacCurrentMetrics { get; init; }

    /// <summary>
    /// Per-metric comparison outcome between baseline and post-conversation snapshots.
    /// </summary>
    public List<MacMetricComparisonMetadata>? MacMetricComparisons { get; init; }

    /// <summary>
    /// Whether conversation simulation completion was verified before MAC comparison.
    /// </summary>
    public bool? ConversationStepVerified { get; init; }
}

/// <summary>
/// Comparison details for a single MAC metric.
/// </summary>
public sealed class MacMetricComparisonMetadata
{
    /// <summary>Canonical metric key (e.g., kpi.invocations.rl7).</summary>
    public string MetricKey { get; init; } = string.Empty;

    /// <summary>Baseline value.</summary>
    public double Before { get; init; }

    /// <summary>Post-conversation value.</summary>
    public double After { get; init; }

    /// <summary>After - Before.</summary>
    public double Delta { get; init; }

    /// <summary>True when delta is positive.</summary>
    public bool Increased { get; init; }

    /// <summary>True when this metric is the exception-rate metric.</summary>
    public bool IsExceptionRate { get; init; }

    /// <summary>Final pass/fail for this metric after applying rule exceptions.</summary>
    public bool Passed { get; init; }

    /// <summary>Human-readable reason for this comparison result.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Metadata for a single conversation turn.
/// </summary>
public sealed class ConversationTurnMetadata
{
    /// <summary>The message sent to the agent.</summary>
    public string Input { get; init; } = string.Empty;

    /// <summary>HTTP status code returned by /api/messages.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Truncated response body snippet.</summary>
    public string? ResponseSnippet { get; init; }

    /// <summary>Round-trip latency in milliseconds.</summary>
    public long? LatencyMs { get; init; }

    /// <summary>Whether this turn succeeded.</summary>
    public bool Ok { get; init; }

    /// <summary>Error description if the turn failed.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the agent sent a response via the serviceUrl callback. Null if tracking was unavailable.</summary>
    public bool? AgentResponded { get; init; }

    /// <summary>The text content of the agent's callback response, if any.</summary>
    public string? AgentResponseText { get; init; }
}

/// <summary>
/// Permission status for a single resource API in the blueprint registration check.
/// </summary>
public sealed class BlueprintResourcePermission
{
    /// <summary>Display name of the resource (e.g., "Microsoft Graph").</summary>
    public string ResourceName { get; init; } = string.Empty;

    /// <summary>Application ID of the resource.</summary>
    public string ResourceAppId { get; init; } = string.Empty;

    /// <summary>Scopes expected from config.</summary>
    public List<string> ExpectedScopes { get; init; } = new();

    /// <summary>Scopes actually found in Entra inheritable permissions.</summary>
    public List<string> ActualScopes { get; init; } = new();

    /// <summary>Scopes in config but missing from Entra.</summary>
    public List<string> MissingScopes { get; init; } = new();

    /// <summary>Whether admin consent has been granted (from config).</summary>
    public bool? ConsentGranted { get; init; }

    /// <summary>Whether inheritable permissions are configured in Entra for this resource.</summary>
    public bool InheritablePermissionsConfigured { get; init; }

    /// <summary>Whether kind=allAllowed is set for delegated scopes on this resource.</summary>
    public bool ScopesAllAllowed { get; init; }

    /// <summary>Whether kind=allAllowed is set for app roles on this resource.</summary>
    public bool RolesAllAllowed { get; init; }

    /// <summary>App roles actually granted on the blueprint SP for this resource.</summary>
    public List<string> ActualAppRoles { get; init; } = new();

    /// <summary>
    /// Effective inheritance status: true when kind=allAllowed on both sides AND at least one
    /// permission is granted on the blueprint SP for this resource.
    /// </summary>
    public bool EffectiveInheritance { get; init; }
}
