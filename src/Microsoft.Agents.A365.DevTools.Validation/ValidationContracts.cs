// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.DevTools.Validation;

/// <summary>
/// Severity for a validation issue.
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a single validation issue.
/// </summary>
public sealed record ValidationIssue(
    string Code,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error);

/// <summary>
/// Represents the outcome of a validation operation.
/// </summary>
public sealed class ValidationOutcome
{
    public bool IsValid { get; init; }

    public int ExitCode { get; init; }

    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];

    public static ValidationOutcome Success() => new()
    {
        IsValid = true,
        ExitCode = 0
    };

    public static ValidationOutcome Failure(params ValidationIssue[] issues) => new()
    {
        IsValid = false,
        ExitCode = 1,
        Issues = issues
    };
}

/// <summary>
/// Result of loading configuration for validation.
/// </summary>
public sealed record ValidationLoadResult<TConfig>
{
    public bool IsSuccess { get; init; }

    public TConfig? Value { get; init; }

    public int ExitCode { get; init; }

    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];

    public static ValidationLoadResult<TConfig> Success(TConfig value) => new()
    {
        IsSuccess = true,
        Value = value,
        ExitCode = 0
    };

    public static ValidationLoadResult<TConfig> Failure(int exitCode, params ValidationIssue[] issues) => new()
    {
        IsSuccess = false,
        ExitCode = exitCode,
        Issues = issues
    };
}

/// <summary>
/// Orchestrates the CLI validation workflow using delegates supplied by the caller.
/// </summary>
public sealed class CliValidationCoordinator<TConfig>
{
    public required Func<CancellationToken, Task<bool>> ConfigExistsAsync { get; init; }

    public required Func<CancellationToken, Task<ValidationLoadResult<TConfig>>> LoadConfigAsync { get; init; }

    public required Func<TConfig, IReadOnlyList<string>> ValidateConfig { get; init; }

    public required Func<CancellationToken, Task<bool>> RunSystemChecksAsync { get; init; }

    public required Func<TConfig, CancellationToken, Task<bool>> RunConfigChecksAsync { get; init; }

    public required Action<ValidationIssue> ReportIssue { get; init; }

    public async Task<ValidationOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var exitCode = 0;

        var configExists = await ConfigExistsAsync(cancellationToken);
        if (!configExists)
        {
            var issue = new ValidationIssue(
                "CONFIG_FILE_NOT_FOUND",
                "Configuration file not found. Run 'a365 setup all --agent-name <name>' to set up from scratch.");
            issues.Add(issue);
            ReportIssue(issue);
            exitCode = 2;
        }

        TConfig? config = default;
        if (configExists)
        {
            var loadResult = await LoadConfigAsync(cancellationToken);
            issues.AddRange(loadResult.Issues);

            foreach (var issue in loadResult.Issues)
            {
                ReportIssue(issue);
            }

            if (!loadResult.IsSuccess || loadResult.Value is null)
            {
                exitCode = Math.Max(exitCode, loadResult.ExitCode == 0 ? 2 : loadResult.ExitCode);

                if (!await RunSystemChecksAsync(cancellationToken))
                {
                    exitCode = Math.Max(exitCode, 1);
                }

                return new ValidationOutcome
                {
                    IsValid = exitCode == 0,
                    ExitCode = exitCode,
                    Issues = issues
                };
            }

            config = loadResult.Value;

            var configErrors = ValidateConfig(config);
            foreach (var error in configErrors)
            {
                var issue = new ValidationIssue("CONFIG_VALIDATION_FAILED", error);
                issues.Add(issue);
                ReportIssue(issue);
            }

            if (configErrors.Count > 0)
            {
                exitCode = Math.Max(exitCode, 2);
            }
        }

        if (!await RunSystemChecksAsync(cancellationToken))
        {
            exitCode = Math.Max(exitCode, 1);
        }

        if (config is not null && exitCode < 2)
        {
            var configChecksPassed = await RunConfigChecksAsync(config, cancellationToken);
            if (!configChecksPassed)
            {
                exitCode = Math.Max(exitCode, 1);
            }
        }

        return new ValidationOutcome
        {
            IsValid = exitCode == 0,
            ExitCode = exitCode,
            Issues = issues
        };
    }
}

/// <summary>
/// Contract for validation components in the validation subproject.
/// </summary>
public interface IValidator<in T>
{
    Task<ValidationOutcome> ValidateAsync(T value, CancellationToken cancellationToken = default);
}