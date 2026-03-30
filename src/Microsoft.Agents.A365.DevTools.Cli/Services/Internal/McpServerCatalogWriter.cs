// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Internal;

public static class McpServerCatalogWriter
{
    public static string WriteCatalog(string responseContent)
    {
        // V2 endpoint returns a raw JSON array [...].
        // V1 returns { "mcpServers": [...] }.
        // Normalize both to the wrapped format so all callers remain unchanged.
        if (responseContent.TrimStart().StartsWith('['))
        {
            responseContent = $"{{\"mcpServers\":{responseContent}}}";
        }

        var catalogPath = Path.Combine(Path.GetTempPath(), "mcpServerCatalog.json");
        File.WriteAllText(catalogPath, responseContent);
        return catalogPath;
    }

    // Writes the hardcoded V2 catalog when the live V2 endpoint is not yet available.
    // Remove once discoverToolServers?api-version=2 is confirmed live (Q1).
    public static string WriteHardcodedV2Catalog()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), "mcpServerCatalog.json");
        File.WriteAllText(catalogPath, McpConstants.V2Catalog.WrappedJson);
        return catalogPath;
    }

    public static string GetCatalogPath()
    {
        return Path.Combine(Path.GetTempPath(), "mcpServerCatalog.json");
    }
}