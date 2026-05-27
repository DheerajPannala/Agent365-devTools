// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that the agent blueprint is registered in Microsoft Entra ID.
/// Checks that the blueprint application exists, has a service principal,
/// (if configured) has an agent registration, and has inheritable permissions configured.
/// Uses the same Graph API methods as <c>query-entra</c>.
/// </summary>
public class BlueprintRegistrationRequirementCheck : RequirementCheck
{
    private readonly GraphApiService _graphApiService;
    private readonly AgentBlueprintService? _blueprintService;

    public BlueprintRegistrationRequirementCheck(GraphApiService graphApiService, AgentBlueprintService? blueprintService = null)
    {
        _graphApiService = graphApiService ?? throw new ArgumentNullException(nameof(graphApiService));
        _blueprintService = blueprintService;
    }

    /// <inheritdoc />
    public override string Name => "Blueprint Registration";

    /// <inheritdoc />
    public override string Description => "Validates that the agent blueprint is registered in Microsoft Entra ID";

    /// <inheritdoc />
    public override string Category => "Registration";

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
        if (string.IsNullOrWhiteSpace(config.AgentBlueprintId))
        {
            return RequirementCheckResult.Failure(
                "Agent blueprint ID not found in configuration",
                "Run 'a365 setup blueprint' to create and register a blueprint.",
                details: "The agentBlueprintId must be set in a365.generated.config.json before registration can be verified.");
        }

        if (string.IsNullOrWhiteSpace(config.TenantId))
        {
            return RequirementCheckResult.Failure(
                "Tenant ID not found in configuration",
                "Run 'a365 setup all' to configure your tenant ID.",
                details: "The tenantId must be set in a365.config.json before registration can be verified.");
        }

        var blueprintId = config.AgentBlueprintId;
        var tenantId = config.TenantId;

        // Check 1: Blueprint application exists in Entra
        bool appExists;
        try
        {
            appExists = await _graphApiService.ApplicationExistsByAppIdAsync(tenantId, blueprintId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to query Entra for blueprint application");
            return RequirementCheckResult.Warning(
                "Could not verify blueprint application in Entra ID",
                details: $"Graph API query failed: {ex.Message}. Ensure you are authenticated with 'az login'.");
        }

        if (!appExists)
        {
            return RequirementCheckResult.Failure(
                $"Blueprint application '{blueprintId}' not found in Entra ID",
                "Run 'a365 setup blueprint' to create the blueprint application, or verify the agentBlueprintId in your configuration.",
                details: $"No Entra application with appId '{blueprintId}' exists in tenant '{tenantId}'.");
        }

        // Check 2: Service principal exists for the blueprint
        string? servicePrincipalId;
        try
        {
            servicePrincipalId = await _graphApiService.LookupServicePrincipalByAppIdAsync(tenantId, blueprintId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to query Entra for blueprint service principal");
            return RequirementCheckResult.Warning(
                "Blueprint application exists but could not verify service principal",
                details: $"Graph API query failed: {ex.Message}");
        }

        if (string.IsNullOrEmpty(servicePrincipalId))
        {
            return RequirementCheckResult.Failure(
                $"Service principal not found for blueprint '{blueprintId}'",
                "Run 'a365 setup blueprint' to ensure the service principal is provisioned.",
                details: $"Application '{blueprintId}' exists but has no service principal in tenant '{tenantId}'.");
        }

        // Check 3: Agent registration exists (if registrationId is configured)
        if (!string.IsNullOrWhiteSpace(config.AgentRegistrationId))
        {
            bool? registrationExists;
            try
            {
                registrationExists = await _graphApiService.AgentRegistrationExistsAsync(
                    tenantId, config.AgentRegistrationId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Failed to query agent registration");
                return RequirementCheckResult.Warning(
                    "Blueprint and service principal exist but could not verify agent registration",
                    details: $"Agent registry query failed: {ex.Message}");
            }

            if (registrationExists == false)
            {
                return RequirementCheckResult.Failure(
                    $"Agent registration '{config.AgentRegistrationId}' not found",
                    "Run 'a365 setup all' to register the agent, or verify the agentRegistrationId in your configuration.",
                    details: $"Blueprint '{blueprintId}' and service principal exist, but agent registration " +
                        $"'{config.AgentRegistrationId}' was not found in the agent registry.");
            }

            if (registrationExists == null)
            {
                return RequirementCheckResult.Warning(
                    "Blueprint registered but agent registration status is unknown",
                    details: $"Application and service principal verified. Agent registration '{config.AgentRegistrationId}' " +
                        "could not be confirmed (insufficient permissions or transient error).");
            }

            return await BuildSuccessResult(config, blueprintId, tenantId, logger,
                $"Blueprint '{blueprintId}' registered with service principal and agent registration '{config.AgentRegistrationId}'.",
                cancellationToken);
        }

            return await BuildSuccessResult(config, blueprintId, tenantId, logger,
            $"Blueprint '{blueprintId}' registered with service principal '{servicePrincipalId}'.",
            cancellationToken);
    }

    /// <summary>
    /// After core registration checks pass, verify inheritable permissions and consent status
    /// by comparing config.ResourceConsents (expected) against what is actually in Entra.
    /// Missing or mismatched permissions produce a warning (not a failure).
    /// </summary>
    private async Task<RequirementCheckResult> BuildSuccessResult(
            Agent365Config config,
            string blueprintId,
            string tenantId,
            ILogger logger,
            string baseDetails,
            CancellationToken cancellationToken)
    {
            if (_blueprintService is null || config.ResourceConsents.Count == 0)
            {
                return RequirementCheckResult.Success(details: baseDetails);
            }

            List<(string ResourceAppId, List<string> Scopes)> actualPermissions;
            try
            {
                actualPermissions = await _blueprintService.ListInheritablePermissionsAsync(
                    tenantId, blueprintId, ct: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Failed to query inheritable permissions");
                return RequirementCheckResult.Warning(
                    "Blueprint registered but could not verify inheritable permissions",
                    details: $"{baseDetails} Permissions query failed: {ex.Message}");
            }

            var actualByResource = actualPermissions.ToDictionary(
                p => p.ResourceAppId,
                p => new HashSet<string>(p.Scopes, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var warnings = new List<string>();

            foreach (var expected in config.ResourceConsents)
            {
                var resourceLabel = !string.IsNullOrWhiteSpace(expected.ResourceName)
                    ? expected.ResourceName
                    : expected.ResourceAppId;

                if (!actualByResource.TryGetValue(expected.ResourceAppId, out var actualScopes))
                {
                    warnings.Add($"{resourceLabel}: no inheritable permissions configured in Entra");
                    continue;
                }

                var missingScopes = expected.Scopes
                    .Where(s => !actualScopes.Contains(s))
                    .ToList();

                if (missingScopes.Count > 0)
                {
                    warnings.Add($"{resourceLabel}: missing scopes: {string.Join(", ", missingScopes)}");
                }

                if (expected.ConsentGranted is false)
                {
                    warnings.Add($"{resourceLabel}: admin consent not granted");
                }
            }

            if (warnings.Count > 0)
            {
                return RequirementCheckResult.Warning(
                    "Blueprint registered but permissions/consent gaps detected",
                    details: $"{baseDetails} {string.Join(". ", warnings)}. " +
                        "Run 'a365 setup all' or grant consent in the Azure portal.");
            }

            var scopeSummary = string.Join("; ", config.ResourceConsents.Select(r =>
                $"{(string.IsNullOrWhiteSpace(r.ResourceName) ? r.ResourceAppId : r.ResourceName)}: " +
                $"{string.Join(", ", r.Scopes)}"));

            return RequirementCheckResult.Success(
                details: $"{baseDetails} Permissions verified: {scopeSummary}");
    }
}
