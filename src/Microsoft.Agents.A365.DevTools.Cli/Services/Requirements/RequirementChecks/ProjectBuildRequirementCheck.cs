// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that the user's project builds locally with warnings treated as errors.
/// Uses PlatformDetector to determine the project type and runs the appropriate build command.
/// </summary>
public class ProjectBuildRequirementCheck : RequirementCheck
{
    private readonly PlatformDetector _platformDetector;
    private readonly CommandExecutor _commandExecutor;

    public ProjectBuildRequirementCheck(PlatformDetector platformDetector, CommandExecutor commandExecutor)
    {
        _platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
    }

    /// <inheritdoc />
    public override string Name => "Project Build";

    /// <inheritdoc />
    public override string Description => "Validates that the project builds locally with warnings treated as errors";

    /// <inheritdoc />
    public override string Category => "Code Health";

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
        var projectPath = ResolveProjectPath(config);

        if (!Directory.Exists(projectPath))
        {
            return RequirementCheckResult.Failure(
                $"Project path does not exist: {projectPath}",
                "Ensure the project directory exists, or set deploymentProjectPath in a365.config.json");
        }

        var platform = _platformDetector.Detect(projectPath);

        if (platform == ProjectPlatform.Unknown)
        {
            return RequirementCheckResult.Warning(
                "Could not detect project platform, skipping build validation",
                details: $"No .NET, Node.js, or Python project detected in {projectPath}");
        }

        var (command, arguments) = GetBuildCommand(platform);

        logger.LogDebug("Running build check: {Command} {Arguments} in {Path}", command, arguments, projectPath);

        var result = await _commandExecutor.ExecuteAsync(
            command,
            arguments,
            workingDirectory: projectPath,
            captureOutput: true,
            suppressErrorLogging: true,
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            return new RequirementCheckResult
            {
                Passed = true,
                Details = $"{platform} project builds with warnings as errors",
                Metadata = new RequirementCheckMetadata
                {
                    Platform = platform.ToString(),
                    ExitCode = result.ExitCode,
                    Log = TruncateLog(result.StandardOutput)
                }
            };
        }

        var errorSummary = ExtractBuildErrorSummary(result, platform);

        return new RequirementCheckResult
        {
            Passed = false,
            ErrorMessage = $"Project build failed ({platform}):\n{errorSummary}",
            ResolutionGuidance = GetResolutionGuidance(platform),
            Metadata = new RequirementCheckMetadata
            {
                Platform = platform.ToString(),
                ExitCode = result.ExitCode,
                Log = TruncateLog(!string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.StandardOutput)
            }
        };
    }

    private static (string Command, string Arguments) GetBuildCommand(ProjectPlatform platform)
    {
        return platform switch
        {
            ProjectPlatform.DotNet => ("dotnet", "build --no-restore /p:TreatWarningsAsErrors=true"),
            ProjectPlatform.NodeJs => ("npm", "run build"),
            ProjectPlatform.Python => ("python", "-m py_compile ."),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform")
        };
    }

    private static string GetResolutionGuidance(ProjectPlatform platform)
    {
        return platform switch
        {
            ProjectPlatform.DotNet => "Fix the build errors and warnings in your project.\n" +
                "Run 'dotnet build /p:TreatWarningsAsErrors=true' locally to see the full output.",
            ProjectPlatform.NodeJs => "Fix the build errors in your project.\n" +
                "Run 'npm run build' locally to see the full output.",
            ProjectPlatform.Python => "Fix the syntax errors in your Python files.\n" +
                "Run 'python -m py_compile <file>' on each file to check for syntax errors.",
            _ => "Fix the build errors in your project and try again."
        };
    }

    /// <summary>
    /// Extracts a concise summary from build output, limiting to the most relevant lines.
    /// </summary>
    private static string ExtractBuildErrorSummary(CommandResult result, ProjectPlatform platform)
    {
        var output = !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError
            : result.StandardOutput;

        if (string.IsNullOrWhiteSpace(output))
        {
            return $"Build exited with code {result.ExitCode} (no output captured)";
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (platform == ProjectPlatform.DotNet)
        {
            // For .NET, extract lines containing "error" or "warning" (MSBuild output)
            var diagnosticLines = lines
                .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase) ||
                            l.Contains(": warning ", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Trim())
                .Take(10)
                .ToArray();

            if (diagnosticLines.Length > 0)
            {
                return string.Join("\n", diagnosticLines.Select(l => $"  {l}"));
            }
        }

        // Fallback: return last 10 meaningful lines
        var lastLines = lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .TakeLast(10)
            .ToArray();

        return string.Join("\n", lastLines.Select(l => $"  {l}"));
    }

    /// <summary>
    /// Returns deploymentProjectPath if configured, otherwise falls back to the current directory.
    /// </summary>
    private static string ResolveProjectPath(Agent365Config config)
    {
        return string.IsNullOrWhiteSpace(config.DeploymentProjectPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(config.DeploymentProjectPath);
    }

    /// <summary>
    /// Truncates log output to the last 100 lines to keep the JSON report reasonable.
    /// </summary>
    private static string? TruncateLog(string? log, int maxLines = 100)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        var lines = log.Split('\n');
        if (lines.Length <= maxLines)
        {
            return log.TrimEnd();
        }

        return string.Join("\n", lines.TakeLast(maxLines)).TrimEnd();
    }
}
