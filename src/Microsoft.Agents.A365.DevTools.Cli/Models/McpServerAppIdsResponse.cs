// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.DevTools.Cli.Models;

/// <summary>
/// Response model for the GET /agents/mcpServers/byName/{serverName}/appIds endpoint.
/// </summary>
public class McpServerAppIdsResponse
{
    /// <summary>
    /// The PPMI application (client) ID registered for the MCP server.
    /// Used as the resource when creating oauth2PermissionGrants.
    /// </summary>
    [JsonPropertyName("mcpServerAppId")]
    public string? McpServerAppId { get; set; }
}
