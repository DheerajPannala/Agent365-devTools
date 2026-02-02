// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;

namespace Microsoft.Agents.A365.DevTools.Cli.Helpers;

/// <summary>
/// Registry for validating OAuth2 scopes and resolving their corresponding resource App IDs.
/// Provides a centralized, extensible mapping of scope names to resource information.
/// </summary>
/// <remarks>
/// Key design principle: Scope validation is independent of resource ID.
/// The registry maps scopes to resources, but the primary purpose is validation.
/// Resource resolution is a natural byproduct of looking up a scope in the registry.
/// </remarks>
public static class ScopeRegistry
{
    /// <summary>
    /// Attempts to get the resource information for a given scope name.
    /// </summary>
    /// <param name="scopeName">The OAuth2 scope name to look up.</param>
    /// <param name="environment">The environment (e.g., "prod") for resolving environment-specific resource App IDs.</param>
    /// <returns>
    /// A tuple containing (resourceAppId, resourceName) if the scope is recognized,
    /// or (null, null) if the scope is not found in the registry.
    /// </returns>
    /// <remarks>
    /// Scope lookup is case-insensitive.
    /// </remarks>
    public static (string? ResourceAppId, string? ResourceName) TryGetResource(string scopeName, string environment = "prod")
    {
        if (string.IsNullOrWhiteSpace(scopeName))
        {
            return (null, null);
        }

        // Check if scope exists in MCP server scopes (case-insensitive)
        var mcpScopes = McpConstants.ServerScopeMappings.GetAllScopes();
        if (mcpScopes.Any(s => s.Equals(scopeName, StringComparison.OrdinalIgnoreCase)))
        {
            return (ConfigConstants.GetAgent365ToolsResourceAppId(environment), "Agent 365 Tools");
        }

        return (null, null);
    }

    /// <summary>
    /// Validates an array of scopes and resolves their resource information.
    /// </summary>
    /// <param name="scopes">Array of scope names to validate.</param>
    /// <param name="environment">The environment (e.g., "prod") for resolving environment-specific resource App IDs.</param>
    /// <returns>
    /// A validation result containing:
    /// - IsValid: true if all scopes are recognized and belong to the same resource
    /// - ResourceAppId: The resolved resource App ID (null if validation failed)
    /// - ResourceName: The resolved resource display name (null if validation failed)
    /// - UnrecognizedScopes: List of scopes that were not found in the registry
    /// </returns>
    public static ScopeValidationResult ValidateScopes(string[] scopes, string environment = "prod")
    {
        if (scopes == null || scopes.Length == 0)
        {
            return new ScopeValidationResult
            {
                IsValid = false,
                ErrorMessage = "No scopes provided for validation."
            };
        }

        var unrecognizedScopes = new List<string>();
        string? resolvedResourceAppId = null;
        string? resolvedResourceName = null;

        foreach (var scope in scopes)
        {
            var (resourceAppId, resourceName) = TryGetResource(scope, environment);

            if (resourceAppId is null)
            {
                unrecognizedScopes.Add(scope);
                continue;
            }

            if (resolvedResourceAppId is not null && resourceAppId != resolvedResourceAppId)
            {
                // Conflict: multiple resources detected
                return new ScopeValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Scopes belong to multiple resources ({resolvedResourceAppId} and {resourceAppId}), which is not supported."
                };
            }

            // Store the resolved resource (will be used if all scopes map to same resource)
            resolvedResourceAppId = resourceAppId;
            resolvedResourceName = resourceName;
        }

        // Check for unrecognized scopes
        if (unrecognizedScopes.Count > 0)
        {
            return new ScopeValidationResult
            {
                IsValid = false,
                UnrecognizedScopes = unrecognizedScopes,
                ErrorMessage = unrecognizedScopes.Count == 1
                    ? $"Scope '{unrecognizedScopes[0]}' is not recognized."
                    : $"The following scopes are not recognized: {string.Join(", ", unrecognizedScopes)}"
            };
        }

        // All scopes valid and belong to same resource
        return new ScopeValidationResult
        {
            IsValid = true,
            ResourceAppId = resolvedResourceAppId,
            ResourceName = resolvedResourceName
        };
    }
}

/// <summary>
/// Result of scope validation containing resource information or error details.
/// </summary>
public class ScopeValidationResult
{
    /// <summary>
    /// Indicates whether all scopes are valid and belong to the same resource.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// The resolved resource App ID if validation succeeded.
    /// </summary>
    public string? ResourceAppId { get; init; }

    /// <summary>
    /// The resolved resource display name if validation succeeded.
    /// </summary>
    public string? ResourceName { get; init; }

    /// <summary>
    /// List of scopes that were not found in the registry.
    /// </summary>
    public List<string>? UnrecognizedScopes { get; init; }

    /// <summary>
    /// Error message describing why validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
