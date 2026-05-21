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

    [JsonPropertyName("repair")]
    public RepairResult Repair { get; set; } = RepairResult.NotImplemented();

    [JsonPropertyName("summary")]
    public SummaryResult Summary { get; set; } = new();
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
    public TierResult Telemetry { get; set; } = TierResult.CreateSkipped("not yet implemented");

    [JsonPropertyName("blueprint")]
    public TierResult Blueprint { get; set; } = TierResult.CreateSkipped("not yet implemented");

    [JsonPropertyName("mac")]
    public TierResult Mac { get; set; } = TierResult.CreateSkipped("not yet implemented");

    [JsonPropertyName("m365")]
    public TierResult M365 { get; set; } = TierResult.CreateSkipped("not yet implemented");

    [JsonPropertyName("judge")]
    public TierResult Judge { get; set; } = TierResult.CreateSkipped("not yet implemented");
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
/// Repair result (not yet implemented).
/// </summary>
public sealed class RepairResult
{
    [JsonPropertyName("skipped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Skipped { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    [JsonPropertyName("iterations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Iterations { get; set; }

    [JsonPropertyName("patches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Patches { get; set; }

    [JsonPropertyName("finalOk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FinalOk { get; set; }

    public static RepairResult NotImplemented() => new() { Skipped = true, Reason = "not yet implemented" };
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
