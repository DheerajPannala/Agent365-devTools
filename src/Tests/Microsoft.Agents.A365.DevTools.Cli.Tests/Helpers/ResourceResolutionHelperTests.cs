// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Helpers;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Helpers;

/// <summary>
/// Unit tests for ResourceResolutionHelper
/// </summary>
public class ResourceResolutionHelperTests
{
    private const string DefaultEnvironment = "prod";

    #region ResolveByKeyword - MCP keyword tests

    [Fact]
    public void ResolveByKeyword_McpKeyword_ReturnsCorrectAppId()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("mcp", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
    }

    [Fact]
    public void ResolveByKeyword_McpKeyword_ReturnsCorrectDisplayName()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("mcp", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Agent 365 Tools (MCP)");
    }

    [Fact]
    public void ResolveByKeyword_McpKeyword_ReturnsDiscoverEndpointUrl()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("mcp", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.Url.Should().Be(ConfigConstants.GetDiscoverEndpointUrl(DefaultEnvironment));
    }

    #endregion

    #region ResolveByKeyword - PowerPlatform keyword tests

    [Fact]
    public void ResolveByKeyword_PowerPlatformKeyword_ReturnsCorrectAppId()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("powerplatform", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.ResourceAppId.Should().Be(MosConstants.PowerPlatformApiResourceAppId);
    }

    [Fact]
    public void ResolveByKeyword_PowerPlatformKeyword_ReturnsCorrectDisplayName()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("powerplatform", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Power Platform API");
    }

    [Fact]
    public void ResolveByKeyword_PowerPlatformKeyword_ReturnsNullUrl()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("powerplatform", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.Url.Should().BeNull();
    }

    #endregion

    #region ResolveByKeyword - Case insensitivity tests

    [Theory]
    [InlineData("MCP")]
    [InlineData("Mcp")]
    [InlineData("mCp")]
    public void ResolveByKeyword_McpCaseInsensitive_ReturnsCorrectResult(string keyword)
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword(keyword, DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Agent 365 Tools (MCP)");
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
    }

    [Theory]
    [InlineData("POWERPLATFORM")]
    [InlineData("PowerPlatform")]
    [InlineData("Powerplatform")]
    public void ResolveByKeyword_PowerPlatformCaseInsensitive_ReturnsCorrectResult(string keyword)
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword(keyword, DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Power Platform API");
        result.ResourceAppId.Should().Be(MosConstants.PowerPlatformApiResourceAppId);
    }

    #endregion

    #region ResolveByKeyword - Null/empty defaults to MCP

    [Fact]
    public void ResolveByKeyword_NullKeyword_DefaultsToMcp()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword(null, DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Agent 365 Tools (MCP)");
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
    }

    [Fact]
    public void ResolveByKeyword_EmptyKeyword_DefaultsToMcp()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Agent 365 Tools (MCP)");
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
    }

    [Fact]
    public void ResolveByKeyword_WhitespaceKeyword_DefaultsToMcp()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("   ", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Agent 365 Tools (MCP)");
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
    }

    #endregion

    #region ResolveByKeyword - Unknown keyword

    [Fact]
    public void ResolveByKeyword_UnknownKeyword_ReturnsNull()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("unknownresource", DefaultEnvironment);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveByKeyword_RandomString_ReturnsNull()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveByKeyword("graph", DefaultEnvironment);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ResolveByCustomId tests

    [Fact]
    public void ResolveByCustomId_ValidGuid_ReturnsResourceWithCorrectAppId()
    {
        // Arrange
        var customId = "12345678-1234-1234-1234-123456789abc";

        // Act
        var result = ResourceResolutionHelper.ResolveByCustomId(customId);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(customId);
    }

    [Fact]
    public void ResolveByCustomId_ValidGuid_ReturnsGenericDisplayName()
    {
        // Arrange
        var customId = "12345678-1234-1234-1234-123456789abc";

        // Act
        var result = ResourceResolutionHelper.ResolveByCustomId(customId);

        // Assert
        result.Should().NotBeNull();
        result.DisplayName.Should().Be($"Custom Resource ({customId})");
    }

    [Fact]
    public void ResolveByCustomId_ValidGuid_ReturnsNullUrl()
    {
        // Arrange
        var customId = "12345678-1234-1234-1234-123456789abc";

        // Act
        var result = ResourceResolutionHelper.ResolveByCustomId(customId);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().BeNull();
    }

    #endregion

    #region ResolvedResource record tests

    [Fact]
    public void ResolvedResource_WithNullUrl_IsValid()
    {
        // Act
        var resource = new ResolvedResource("app-id", "Display Name", null);

        // Assert
        resource.ResourceAppId.Should().Be("app-id");
        resource.DisplayName.Should().Be("Display Name");
        resource.Url.Should().BeNull();
    }

    #endregion

    #region ResolveResource tests

    [Fact]
    public void ResolveResource_WithValidResourceId_ReturnsCustomResource()
    {
        // Arrange
        var resourceId = "12345678-1234-1234-1234-123456789abc";

        // Act
        var result = ResourceResolutionHelper.ResolveResource(resourceId, null, DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(resourceId);
        result.DisplayName.Should().Be($"Custom Resource ({resourceId})");
        result.Url.Should().BeNull();
    }

    [Fact]
    public void ResolveResource_WithInvalidResourceId_ThrowsArgumentException()
    {
        // Arrange
        var invalidResourceId = "not-a-guid";

        // Act
        var act = () => ResourceResolutionHelper.ResolveResource(invalidResourceId, null, DefaultEnvironment);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid resource application ID*");
    }

    [Fact]
    public void ResolveResource_WithValidResourceKeyword_ReturnsResource()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveResource(null, "mcp", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
        result.DisplayName.Should().Be("Agent 365 Tools (MCP)");
    }

    [Fact]
    public void ResolveResource_WithUnknownResourceKeyword_ThrowsArgumentException()
    {
        // Act
        var act = () => ResourceResolutionHelper.ResolveResource(null, "unknown", DefaultEnvironment);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown resource keyword*");
    }

    [Fact]
    public void ResolveResource_WithBothResourceIdAndKeyword_ThrowsArgumentException()
    {
        // Arrange
        var resourceId = "12345678-1234-1234-1234-123456789abc";
        var resourceKeyword = "mcp";

        // Act
        var act = () => ResourceResolutionHelper.ResolveResource(resourceId, resourceKeyword, DefaultEnvironment);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot specify both*");
    }

    [Fact]
    public void ResolveResource_WithNeitherResourceIdNorKeyword_DefaultsToMcp()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveResource(null, null, DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
        result.DisplayName.Should().Be("Agent 365 Tools (MCP)");
    }

    [Fact]
    public void ResolveResource_WithEmptyResourceKeyword_DefaultsToMcp()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveResource(null, "", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(ConfigConstants.GetAgent365ToolsResourceAppId(DefaultEnvironment));
        result.DisplayName.Should().Be("Agent 365 Tools (MCP)");
    }

    [Fact]
    public void ResolveResource_WithPowerPlatformKeyword_ReturnsPowerPlatformResource()
    {
        // Act
        var result = ResourceResolutionHelper.ResolveResource(null, "powerplatform", DefaultEnvironment);

        // Assert
        result.Should().NotBeNull();
        result.ResourceAppId.Should().Be(MosConstants.PowerPlatformApiResourceAppId);
        result.DisplayName.Should().Be("Power Platform API");
        result.Url.Should().BeNull();
    }

    #endregion

    #region ErrorMessages constant test

    [Fact]
    public void ErrorMessages_UnknownResourceKeyword_ContainsExpectedContent()
    {
        // Assert
        ErrorMessages.UnknownResourceKeyword.Should().Contain("mcp");
        ErrorMessages.UnknownResourceKeyword.Should().Contain("powerplatform");
        ErrorMessages.UnknownResourceKeyword.Should().Contain("{0}");
    }

    #endregion
}
