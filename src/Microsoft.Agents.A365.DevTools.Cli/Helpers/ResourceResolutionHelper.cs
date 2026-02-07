// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;

namespace Microsoft.Agents.A365.DevTools.Cli.Helpers;

/// <summary>
/// Represents a resolved resource with its application ID, display name, and optional URL.
/// </summary>
/// <param name="ResourceAppId">The Azure AD application ID for the resource.</param>
/// <param name="DisplayName">A human-readable display name for the resource.</param>
/// <param name="Url">An optional URL associated with the resource (e.g., endpoint URL).</param>
public record ResolvedResource(string ResourceAppId, string DisplayName, string? Url);

/// <summary>
/// Provides shared resource keyword resolution logic for CLI subcommands.
/// Maps well-known resource keywords (e.g., "mcp", "powerplatform") to their
/// corresponding Azure AD application IDs, display names, and optional URLs.
/// </summary>
public static class ResourceResolutionHelper
{
    /// <summary>
    /// Resolves a resource keyword to its corresponding resource application info.
    /// Defaults to "mcp" when the keyword is null or empty.
    /// </summary>
    /// <param name="keyword">The resource keyword (e.g., "mcp", "powerplatform"). Defaults to "mcp" when null or empty.</param>
    /// <param name="environment">The environment to use for environment-aware resource resolution (e.g., "prod").</param>
    /// <returns>A <see cref="ResolvedResource"/> containing the app ID, display name, and optional URL, or null if the keyword is unknown.</returns>
    public static ResolvedResource? ResolveByKeyword(string? keyword, string environment)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            keyword = "mcp";
        }

        return keyword.ToLowerInvariant() switch
        {
            "mcp" => new ResolvedResource(
                ConfigConstants.GetAgent365ToolsResourceAppId(environment),
                "Agent 365 Tools (MCP)",
                ConfigConstants.GetDiscoverEndpointUrl(environment)),
            "powerplatform" => new ResolvedResource(
                MosConstants.PowerPlatformApiResourceAppId,
                "Power Platform API",
                null),
            _ => null
        };
    }

    /// <summary>
    /// Wraps a custom resource application ID in a <see cref="ResolvedResource"/> with a generic display name.
    /// Used when the caller provides an explicit resource GUID rather than a keyword.
    /// </summary>
    /// <param name="resourceId">The custom resource application ID (GUID).</param>
    /// <returns>A <see cref="ResolvedResource"/> with a generic display name and no URL.</returns>
    public static ResolvedResource ResolveByCustomId(string resourceId)
    {
        return new ResolvedResource(resourceId, $"Custom Resource ({resourceId})", null);
    }
}
