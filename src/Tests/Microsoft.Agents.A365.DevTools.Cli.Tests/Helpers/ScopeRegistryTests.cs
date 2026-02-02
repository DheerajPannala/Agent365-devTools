// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Agents.A365.DevTools.Cli.Helpers;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Helpers;

/// <summary>
/// Unit tests for ScopeRegistry helper class.
/// Tests validate scope lookup, resource resolution, and validation behavior.
/// </summary>
public class ScopeRegistryTests
{
    #region TryGetResource Tests

    [Fact]
    public void TryGetResource_WithValidMcpScope_ReturnsResourceInfo()
    {
        // Arrange
        var validScope = "McpServers.Mail.All";

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(validScope);

        // Assert
        resourceAppId.Should().NotBeNull();
        resourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        resourceName.Should().Be("Agent 365 Tools");
    }

    [Fact]
    public void TryGetResource_WithCalendarScope_ReturnsResourceInfo()
    {
        // Arrange
        var validScope = "McpServers.Calendar.All";

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(validScope);

        // Assert
        resourceAppId.Should().NotBeNull();
        resourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        resourceName.Should().Be("Agent 365 Tools");
    }

    [Fact]
    public void TryGetResource_WithTeamsScope_ReturnsResourceInfo()
    {
        // Arrange
        var validScope = "McpServers.Teams.All";

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(validScope);

        // Assert
        resourceAppId.Should().NotBeNull();
        resourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        resourceName.Should().Be("Agent 365 Tools");
    }

    [Fact]
    public void TryGetResource_WithUnrecognizedScope_ReturnsNull()
    {
        // Arrange
        var invalidScope = "SomeInvalidScope.DoesNotExist";

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(invalidScope);

        // Assert
        resourceAppId.Should().BeNull();
        resourceName.Should().BeNull();
    }

    [Fact]
    public void TryGetResource_WithEmptyString_ReturnsNull()
    {
        // Arrange
        var emptyScope = string.Empty;

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(emptyScope);

        // Assert
        resourceAppId.Should().BeNull();
        resourceName.Should().BeNull();
    }

    [Fact]
    public void TryGetResource_WithNullScope_ReturnsNull()
    {
        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(null!);

        // Assert
        resourceAppId.Should().BeNull();
        resourceName.Should().BeNull();
    }

    [Fact]
    public void TryGetResource_WithWhitespace_ReturnsNull()
    {
        // Arrange
        var whitespaceScope = "   ";

        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(whitespaceScope);

        // Assert
        resourceAppId.Should().BeNull();
        resourceName.Should().BeNull();
    }

    [Theory]
    [InlineData("mcpservers.mail.all")]
    [InlineData("MCPSERVERS.MAIL.ALL")]
    [InlineData("McpServers.MAIL.all")]
    [InlineData("mcpservers.MAIL.ALL")]
    public void TryGetResource_IsCaseInsensitive(string scopeName)
    {
        // Act
        var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(scopeName);

        // Assert
        resourceAppId.Should().NotBeNull();
        resourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        resourceName.Should().Be("Agent 365 Tools");
    }

    [Fact]
    public void TryGetResource_WithEnvironmentParameter_UsesEnvironmentForResourceResolution()
    {
        // Arrange
        var validScope = "McpServers.Mail.All";

        // Act
        var (resourceAppId, _) = ScopeRegistry.TryGetResource(validScope, "prod");

        // Assert
        resourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
    }

    #endregion

    #region ValidateScopes Tests

    [Fact]
    public void ValidateScopes_WithSingleValidScope_ReturnsValidResult()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ResourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        result.ResourceName.Should().Be("Agent 365 Tools");
        result.UnrecognizedScopes.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateScopes_WithMultipleValidScopes_ReturnsValidResult()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All", "McpServers.Calendar.All", "McpServers.Teams.All" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ResourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
        result.ResourceName.Should().Be("Agent 365 Tools");
        result.UnrecognizedScopes.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateScopes_WithSingleUnrecognizedScope_ReturnsInvalidResult()
    {
        // Arrange
        var scopes = new[] { "InvalidScope.DoesNotExist" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ResourceAppId.Should().BeNull();
        result.UnrecognizedScopes.Should().NotBeNull();
        result.UnrecognizedScopes.Should().Contain("InvalidScope.DoesNotExist");
        result.ErrorMessage.Should().Contain("InvalidScope.DoesNotExist");
        result.ErrorMessage.Should().Contain("not recognized");
    }

    [Fact]
    public void ValidateScopes_WithMultipleUnrecognizedScopes_ReturnsAllUnrecognized()
    {
        // Arrange
        var scopes = new[] { "Invalid.One", "Invalid.Two", "Invalid.Three" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeFalse();
        result.UnrecognizedScopes.Should().HaveCount(3);
        result.UnrecognizedScopes.Should().Contain("Invalid.One");
        result.UnrecognizedScopes.Should().Contain("Invalid.Two");
        result.UnrecognizedScopes.Should().Contain("Invalid.Three");
        result.ErrorMessage.Should().Contain("not recognized");
    }

    [Fact]
    public void ValidateScopes_WithMixedValidAndInvalidScopes_ReturnsInvalidWithUnrecognized()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All", "InvalidScope.NotFound" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeFalse();
        result.UnrecognizedScopes.Should().NotBeNull();
        result.UnrecognizedScopes.Should().Contain("InvalidScope.NotFound");
        result.UnrecognizedScopes.Should().NotContain("McpServers.Mail.All");
        result.ErrorMessage.Should().Contain("InvalidScope.NotFound");
    }

    [Fact]
    public void ValidateScopes_WithEmptyArray_ReturnsInvalidResult()
    {
        // Arrange
        var scopes = Array.Empty<string>();

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No scopes provided");
    }

    [Fact]
    public void ValidateScopes_WithNullArray_ReturnsInvalidResult()
    {
        // Act
        var result = ScopeRegistry.ValidateScopes(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No scopes provided");
    }

    [Fact]
    public void ValidateScopes_WithEnvironmentParameter_PassesEnvironmentToResolution()
    {
        // Arrange
        var scopes = new[] { "McpServers.Mail.All" };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes, "prod");

        // Assert
        result.IsValid.Should().BeTrue();
        result.ResourceAppId.Should().Be(McpConstants.Agent365ToolsProdAppId);
    }

    [Theory]
    [InlineData("mcpservers.mail.all")]
    [InlineData("MCPSERVERS.MAIL.ALL")]
    public void ValidateScopes_IsCaseInsensitive(string scopeName)
    {
        // Arrange
        var scopes = new[] { scopeName };

        // Act
        var result = ScopeRegistry.ValidateScopes(scopes);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ResourceAppId.Should().NotBeNull();
    }

    #endregion

    #region ScopeValidationResult Tests

    [Fact]
    public void ScopeValidationResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new ScopeValidationResult();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ResourceAppId.Should().BeNull();
        result.ResourceName.Should().BeNull();
        result.UnrecognizedScopes.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Integration with McpConstants Tests

    [Fact]
    public void TryGetResource_AllMcpScopes_AreRecognized()
    {
        // Arrange
        var allMcpScopes = McpConstants.ServerScopeMappings.GetAllScopes();

        // Act & Assert
        foreach (var scope in allMcpScopes)
        {
            var (resourceAppId, resourceName) = ScopeRegistry.TryGetResource(scope);
            resourceAppId.Should().NotBeNull($"Scope '{scope}' should be recognized");
            resourceName.Should().NotBeNull($"Scope '{scope}' should have a resource name");
        }
    }

    [Fact]
    public void ValidateScopes_AllMcpScopes_AreValid()
    {
        // Arrange
        var allMcpScopes = McpConstants.ServerScopeMappings.GetAllScopes();

        // Act
        var result = ScopeRegistry.ValidateScopes(allMcpScopes);

        // Assert
        result.IsValid.Should().BeTrue("All MCP scopes should be valid");
        result.ResourceAppId.Should().NotBeNull();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion
}
