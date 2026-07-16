// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.DevTools.Cli.Models;

/// <summary>
/// Response model for an MCP server unpublish operation.
/// </summary>
public class UnpublishMcpServerResponse
{
    /// <summary>
    /// Status of the unpublish operation.
    /// </summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>
    /// Message from the API response.
    /// </summary>
    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    /// <summary>
    /// Entra app registrations the platform created for this server that the CLI must delete: the
    /// platform's identity cannot delete app registrations in the customer tenant, so it returns them
    /// here for the CLI to clean up. Currently the server's Public Clients app; empty/null when the
    /// server had none (for example OOB Dataverse servers or legacy records).
    /// </summary>
    [JsonPropertyName("AppIdsToCleanup")]
    public List<McpServerAppEntry>? AppIdsToCleanup { get; set; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Status?.Equals("Success", StringComparison.OrdinalIgnoreCase) ?? false;
}
