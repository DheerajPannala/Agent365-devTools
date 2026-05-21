// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Exceptions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Agents.A365.DevTools.Validation;

namespace Microsoft.Agents.A365.DevTools.Cli.Commands;

/// <summary>
/// Validates the local Agent 365 CLI configuration and prerequisite state.
/// Writes a structured report to a365.validate.json.
/// </summary>
public sealed class ValidateCommand
{
    internal const string ReportFileName = "a365.validate.json";

    // Status markers — use characters supported across Windows/macOS/Linux terminals
    private const string PassMark = "\u221A";  // √ (square root, same as Windows renders for checkmark)
    private const string FailMark = "X";
    private const string SkipMark = "-";

    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static Command CreateCommand(
        ILogger<ValidateCommand> logger,
        IConfigService configService,
        PlatformDetector? platformDetector = null,
        CommandExecutor? commandExecutor = null,
        IProcessService? processService = null,
        IEnumerable<IRequirementCheck>? requirementChecksOverride = null)
    {
        var command = new Command(CommandNames.Validate,
            "Validate the local Agent 365 CLI configuration and prerequisite state\n" +
            "Checks config validity and code health. Run 'a365 setup all' before using this command.");

        var playgroundOption = new Option<bool>(
            "--playground",
            "Launch AgentsPlayground after automated conversation turns for interactive testing");
        command.AddOption(playgroundOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var ct = context.GetCancellationToken();
            var cwd = Directory.GetCurrentDirectory();
            var configPath = Path.Combine(cwd, ConfigConstants.DefaultConfigFileName);
            var report = new ValidateReport();
            var launchPlayground = context.ParseResult.GetValueForOption(playgroundOption);

            try
            {
                // Phase 1: Config validation (structural tier)
                var (config, configOk) = await ValidateConfigAsync(configService, configPath, logger, report);

                if (!configOk || config is null)
                {
                    report.Summary = new SummaryResult { Ok = false, Blocker = "structural" };
                    context.ExitCode = 1;
                    return;
                }

                logger.LogDebug("Configuration file validated successfully");

                // Populate agent info from config
                var projectPath = ResolveProjectPath(config);
                var language = platformDetector?.Detect(projectPath);
                report.Agent = new AgentInfo
                {
                    Path = projectPath,
                    Language = language is not null and not ProjectPlatform.Unknown
                        ? language.Value.ToString().ToLowerInvariant()
                        : null
                };

                // Phase 2: Run requirement checks and map to tiers
                var checks = requirementChecksOverride?.ToList()
                    ?? BuildValidationChecks(platformDetector, commandExecutor, processService, includeConversation: false);

                var results = await RunChecksDetailedAsync(checks, config, logger, ct);
                MapResultsToTiers(results, report);

                // Phase 2b: Run conversation check only if boot tier passed
                var bootPassed = report.Tiers.Boot is { Skipped: false, Ok: true };
                if (bootPassed && requirementChecksOverride is null)
                {
                    var conversationChecks = BuildConversationChecks(platformDetector, processService, launchPlayground);
                    if (conversationChecks.Count > 0)
                    {
                        var conversationResults = await RunChecksDetailedAsync(conversationChecks, config, logger, ct);
                        MapResultsToTiers(conversationResults, report);
                        results.AddRange(conversationResults);
                    }
                }
                else if (!bootPassed && report.Tiers.Boot is not { Skipped: true })
                {
                    report.Tiers.Conversation = new ConversationTierResult
                    {
                        Skipped = true,
                        Reason = "boot tier failed"
                    };
                }

                // For test overrides, also map conversation checks
                if (requirementChecksOverride is not null)
                {
                    // Conversation checks from override are already in results via MapResultsToTiers
                }

                // Phase 3: Build summary — any failed check is a blocker
                var anyFailed = results.Any(r => !r.Result.Passed);
                var blocker = FindBlocker(report.Tiers);
                report.Summary = new SummaryResult
                {
                    Ok = !anyFailed && blocker is null,
                    Blocker = blocker
                };

                context.ExitCode = report.Summary.Ok ? 0 : 1;

                // Print formatted summary to console
                PrintSummary(report, logger);
            }
            finally
            {
                await WriteReportAsync(report, cwd, logger);
            }
        });

        return command;
    }

    private static async Task<(Agent365Config? Config, bool Ok)> ValidateConfigAsync(
        IConfigService configService,
        string configPath,
        ILogger logger,
        ValidateReport report)
    {
        var structuralChecks = new List<StructuralCheck>();

        if (!await configService.ConfigExistsAsync(configPath))
        {
            structuralChecks.Add(new StructuralCheck { Name = "config-exists", Ok = false, Message = "a365.config.json not found" });
            report.Tiers.Structural = new StructuralTierResult { Ok = false, Checks = structuralChecks };

            logger.LogError("Fail: Configuration File");
            logger.LogInformation("  {Message}", "a365.config.json not found in the current directory.");
            logger.LogInformation("");
            logger.LogInformation("  {Step}", "Run 'a365 setup all --agent-name <name>' to set up first.");
            return (null, false);
        }

        structuralChecks.Add(new StructuralCheck { Name = "config-exists", Ok = true });

        Agent365Config config;
        try
        {
            config = await configService.LoadAsync(configPath);
        }
        catch (ConfigurationValidationException ex)
        {
            structuralChecks.Add(new StructuralCheck { Name = "config-format", Ok = false, Message = ex.IssueDescription });
            report.Tiers.Structural = new StructuralTierResult { Ok = false, Checks = structuralChecks };
            logger.LogError("Fail: Configuration File");
            logger.LogInformation("  {Message}", ex.IssueDescription);
            return (null, false);
        }
        catch (ConfigFileNotFoundException ex)
        {
            structuralChecks.Add(new StructuralCheck { Name = "config-format", Ok = false, Message = ex.IssueDescription });
            report.Tiers.Structural = new StructuralTierResult { Ok = false, Checks = structuralChecks };
            logger.LogError("Fail: Configuration File");
            logger.LogInformation("  {Message}", ex.IssueDescription);
            return (null, false);
        }
        catch (JsonException)
        {
            structuralChecks.Add(new StructuralCheck { Name = "config-format", Ok = false, Message = ErrorMessages.InvalidConfigFormat });
            report.Tiers.Structural = new StructuralTierResult { Ok = false, Checks = structuralChecks };
            logger.LogError("Fail: Configuration File");
            logger.LogInformation("  {Message}", ErrorMessages.InvalidConfigFormat);
            return (null, false);
        }

        structuralChecks.Add(new StructuralCheck { Name = "config-format", Ok = true });

        var configErrors = config.Validate();
        if (configErrors.Count > 0)
        {
            structuralChecks.Add(new StructuralCheck
            {
                Name = "config-schema",
                Ok = false,
                Message = string.Join("; ", configErrors)
            });
            report.Tiers.Structural = new StructuralTierResult { Ok = false, Checks = structuralChecks };

            logger.LogError("Fail: Configuration File");
            foreach (var error in configErrors)
            {
                logger.LogInformation("  {Message}", error);
            }
            logger.LogInformation("");
            logger.LogInformation("  {Step}", "Fix the configuration errors in a365.config.json and try again.");
            return (null, false);
        }

        structuralChecks.Add(new StructuralCheck { Name = "config-schema", Ok = true });

        report.Tiers.Structural = new StructuralTierResult
        {
            Ok = true,
            Checks = structuralChecks
        };

        return (config, true);
    }

    private static async Task<List<(IRequirementCheck Check, RequirementCheckResult Result)>> RunChecksDetailedAsync(
        List<IRequirementCheck> checks,
        Agent365Config config,
        ILogger logger,
        CancellationToken ct)
    {
        var results = new List<(IRequirementCheck Check, RequirementCheckResult Result)>();

        logger.LogDebug("Checking requirements...");

        foreach (var check in checks)
        {
            var result = await check.CheckAsync(config, logger, ct);
            results.Add((check, result));
        }

        var passed = results.Count(r => r.Result.Passed && !r.Result.IsWarning);
        var warnings = results.Count(r => r.Result.IsWarning);
        var failed = results.Count(r => !r.Result.Passed);

        logger.LogDebug("Requirements: {Passed} passed, {Warning} warnings, {Failed} failed",
            passed, warnings, failed);

        return results;
    }

    private static void MapResultsToTiers(
        List<(IRequirementCheck Check, RequirementCheckResult Result)> results,
        ValidateReport report)
    {
        foreach (var (check, result) in results)
        {
            switch (check)
            {
                case ToolingManifestRequirementCheck:
                    // Add to structural tier
                    var structural = report.Tiers.Structural;
                    if (structural.Skipped)
                    {
                        structural = new StructuralTierResult { Ok = true, Checks = new List<StructuralCheck>() };
                        report.Tiers.Structural = structural;
                    }
                    structural.Checks ??= new List<StructuralCheck>();
                    structural.Checks.Add(new StructuralCheck
                    {
                        Name = "tooling-manifest",
                        Ok = result.Passed,
                        Message = result.Passed ? result.Details : result.ErrorMessage
                    });
                    if (!result.Passed)
                    {
                        structural.Ok = false;
                    }
                    break;

                case ProjectBuildRequirementCheck:
                    if (result.IsWarning)
                    {
                        report.Tiers.Build = new BuildTierResult
                        {
                            Skipped = true,
                            Reason = result.ErrorMessage ?? result.Details
                        };
                    }
                    else
                    {
                        report.Tiers.Build = new BuildTierResult
                        {
                            Ok = result.Passed,
                            Log = result.Metadata?.Log,
                            ExitCode = result.Metadata?.ExitCode
                        };
                    }
                    break;

                case LocalRuntimeRequirementCheck:
                    if (result.IsWarning)
                    {
                        report.Tiers.Boot = new BootTierResult
                        {
                            Skipped = true,
                            Reason = result.ErrorMessage ?? result.Details
                        };
                    }
                    else
                    {
                        report.Tiers.Boot = new BootTierResult
                        {
                            Ok = result.Passed,
                            Port = result.Metadata?.Port,
                            BootMs = result.Metadata?.BootMs
                        };
                    }
                    break;

                case ConversationRequirementCheck:
                    if (result.IsWarning)
                    {
                        report.Tiers.Conversation = new ConversationTierResult
                        {
                            Skipped = true,
                            Reason = result.ErrorMessage ?? result.Details
                        };
                    }
                    else
                    {
                        report.Tiers.Conversation = new ConversationTierResult
                        {
                            Ok = result.Passed,
                            PlaygroundLaunched = result.Metadata?.PlaygroundLaunched,
                            Turns = result.Metadata?.Turns?.Select(t => new ConversationTurnResult
                            {
                                Input = t.Input,
                                StatusCode = t.StatusCode,
                                ResponseSnippet = t.ResponseSnippet,
                                LatencyMs = t.LatencyMs,
                                Ok = t.Ok,
                                Error = t.Error,
                                AgentResponded = t.AgentResponded,
                                AgentResponseText = t.AgentResponseText
                            }).ToList()
                        };
                    }
                    break;
            }
        }
    }

    private static string? FindBlocker(ValidationTiers tiers)
    {
        if (tiers.Structural is { Skipped: false, Ok: false }) return "structural";
        if (tiers.Build is { Skipped: false, Ok: false }) return "build";
        if (tiers.Boot is { Skipped: false, Ok: false }) return "boot";
        if (tiers.Conversation is { Skipped: false, Ok: false }) return "conversation";
        if (tiers.Telemetry is { Skipped: false, Ok: false }) return "telemetry";
        if (tiers.Blueprint is { Skipped: false, Ok: false }) return "blueprint";
        if (tiers.Mac is { Skipped: false, Ok: false }) return "mac";
        if (tiers.M365 is { Skipped: false, Ok: false }) return "m365";
        if (tiers.Judge is { Skipped: false, Ok: false }) return "judge";
        return null;
    }

    internal static void PrintSummary(ValidateReport report, ILogger logger)
    {
        logger.LogInformation("");

        // Group related tiers into user-facing rows
        var rows = BuildDisplayRows(report);

        int passCount = 0;
        int failCount = 0;
        int localChecks = 0;

        foreach (var row in rows)
        {
            if (row.Skipped)
            {
                var reason = row.Reason ?? "not configured";
                logger.LogInformation("  {Skip}  {Name,-20} skipped ({Reason})", SkipMark, row.Label, reason);
            }
            else if (row.Ok)
            {
                passCount++;
                localChecks++;
                logger.LogInformation("  {Pass} {Name,-20} {Description}", PassMark, row.Label, row.Description);
            }
            else
            {
                failCount++;
                localChecks++;
                logger.LogInformation("  {Fail} {Name,-20} {Description}", FailMark, row.Label, row.Description);

                if (row.Suggestion is not null)
                {
                    logger.LogInformation("       -> suggestion: {Suggestion}", row.Suggestion);
                }
            }
        }

        logger.LogInformation("");

        if (failCount == 0 && localChecks > 0)
        {
            logger.LogInformation("  All {PassCount} checks passed.", passCount);
        }
        else if (failCount > 0)
        {
            logger.LogInformation(
                "  {FailCount} of {LocalChecks} checks failed.  Run `a365 validate --fix` to attempt auto-repair.",
                failCount, localChecks);
        }

        logger.LogInformation("");
    }

    private static List<DisplayRow> BuildDisplayRows(ValidateReport report)
    {
        var rows = new List<DisplayRow>();
        var tiers = report.Tiers;

        // Row 1: Code health (structural + build + manifest)
        var codeHealthTiers = new[] { tiers.Structural, tiers.Build as TierResult };
        var codeHealthActive = codeHealthTiers.Where(t => !t.Skipped).ToList();
        if (codeHealthActive.Count > 0)
        {
            var allOk = codeHealthActive.All(t => t.Ok == true);
            rows.Add(new DisplayRow
            {
                Label = "Code health",
                Ok = allOk,
                Description = allOk ? "project structure, manifest, build" : "code health check failed",
                Suggestion = allOk ? null : "fix build errors and re-run `a365 validate`"
            });
        }
        else
        {
            rows.Add(new DisplayRow { Label = "Code health", Skipped = true, Reason = "not configured" });
        }

        // Row 2: Boot (api/health)
        if (!tiers.Boot.Skipped)
        {
            var bootOk = tiers.Boot.Ok == true;
            rows.Add(new DisplayRow
            {
                Label = "Runs locally",
                Ok = bootOk,
                Description = bootOk
                    ? $"/api/health OK{(tiers.Boot is BootTierResult b && b.Port is not null ? $" (port {b.Port})" : "")}"
                    : "health check failed",
                Suggestion = bootOk ? null : "ensure the agent starts locally with `dotnet run` or `npm start`"
            });
        }
        else
        {
            rows.Add(new DisplayRow
            {
                Label = "Runs locally",
                Skipped = true,
                Reason = tiers.Boot.Reason ?? "boot skipped"
            });
        }

        // Row 3: Conversation
        if (!tiers.Conversation.Skipped)
        {
            var conv = tiers.Conversation;
            var convOk = conv.Ok == true;
            var turnCount = conv.Turns?.Count ?? 0;
            var respondedCount = conv.Turns?.Count(t => t.AgentResponded == true) ?? 0;
            var failedCount = conv.Turns?.Count(t => !t.Ok) ?? 0;

            rows.Add(new DisplayRow
            {
                Label = "Conversation",
                Ok = convOk,
                Description = convOk
                    ? $"{turnCount}-turn conversation OK, {respondedCount} agent responses"
                    : $"{turnCount}-turn conversation, {failedCount} failed",
                Suggestion = convOk ? null : "check agent logs or a365.validate.json for details"
            });
        }
        else
        {
            rows.Add(new DisplayRow
            {
                Label = "Conversation",
                Skipped = true,
                Reason = tiers.Conversation.Reason ?? "boot tier failed"
            });
        }

        // Remaining individual tiers
        rows.Add(CreateTierRow("Telemetry", tiers.Telemetry,
            "tracing and observability",
            "re-run \"instrument-observability\" skill"));
        rows.Add(CreateTierRow("Registered", tiers.Blueprint,
            "blueprint registration",
            null));
        rows.Add(CreateTierRow("Visible in MAC", tiers.Mac,
            "app compliance checks",
            null));
        rows.Add(CreateTierRow("Visible in M365", tiers.M365,
            "Teams/M365 visibility",
            null));

        return rows;
    }

    private static DisplayRow CreateTierRow(string label, TierResult tier, string description, string? suggestion)
    {
        if (tier.Skipped)
        {
            return new DisplayRow { Label = label, Skipped = true, Reason = tier.Reason ?? "not yet implemented" };
        }

        return new DisplayRow
        {
            Label = label,
            Ok = tier.Ok == true,
            Description = tier.Ok == true ? description : (tier.Reason ?? tier.Warning ?? "check failed"),
            Suggestion = tier.Ok == true ? null : suggestion
        };
    }

    private sealed class DisplayRow
    {
        public string Label { get; init; } = string.Empty;
        public bool Skipped { get; init; }
        public string? Reason { get; init; }
        public bool Ok { get; init; }
        public string? Description { get; init; }
        public string? Suggestion { get; init; }
    }

    private static async Task WriteReportAsync(ValidateReport report, string directory, ILogger logger)
    {
        try
        {
            var reportPath = Path.Combine(directory, ReportFileName);
            var json = JsonSerializer.Serialize(report, ReportSerializerOptions);
            await File.WriteAllTextAsync(reportPath, json);
            logger.LogInformation("Report written to {ReportPath}", reportPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write validation report");
        }
    }

    private static string ResolveProjectPath(Agent365Config config)
    {
        return string.IsNullOrWhiteSpace(config.DeploymentProjectPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(config.DeploymentProjectPath);
    }

    private static List<IRequirementCheck> BuildValidationChecks(
        PlatformDetector? platformDetector,
        CommandExecutor? commandExecutor,
        IProcessService? processService,
        bool includeConversation = false)
    {
        var checks = new List<IRequirementCheck>
        {
            new ToolingManifestRequirementCheck()
        };

        if (platformDetector is not null && commandExecutor is not null)
        {
            checks.Add(new ProjectBuildRequirementCheck(platformDetector, commandExecutor));
        }

        if (platformDetector is not null && processService is not null)
        {
            checks.Add(new LocalRuntimeRequirementCheck(platformDetector, processService));

            if (includeConversation)
            {
                checks.Add(new ConversationRequirementCheck(platformDetector, processService));
            }
        }

        return checks;
    }

    private static List<IRequirementCheck> BuildConversationChecks(
        PlatformDetector? platformDetector,
        IProcessService? processService,
        bool launchPlayground = false)
    {
        var checks = new List<IRequirementCheck>();

        if (platformDetector is not null && processService is not null)
        {
            checks.Add(new ConversationRequirementCheck(
                platformDetector, processService, launchPlayground: launchPlayground));
        }

        return checks;
    }
}