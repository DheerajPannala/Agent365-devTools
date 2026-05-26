// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that the agent blueprint is registered in Microsoft Entra ID.
/// Checks that the blueprint application exists, has a service principal, and
/// (if configured) has an agent registration in the Microsoft Agent Registry.
/// Uses the same Graph API methods as <c>query-entra</c>.
/// </summary>
public class BlueprintRegistrationRequirementCheck : RequirementCheck
{
    private readonly GraphApiService _graphApiService;

    public BlueprintRegistrationRequirementCheck(GraphApiService graphApiService)
    {
        _graphApiService = graphApiService ?? throw new ArgumentNullException(nameof(graphApiService));
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

            return RequirementCheckResult.Success(
                details: $"Blueprint '{blueprintId}' registered with service principal and agent registration '{config.AgentRegistrationId}'.");
        }

        return RequirementCheckResult.Success(
            details: $"Blueprint '{blueprintId}' registered with service principal '{servicePrincipalId}'.");
    }
}
