// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Commands.DevelopSubcommands;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Commands;

/// <summary>
/// Unit tests for GetToken subcommand
/// </summary>
[Collection("Sequential")]
public class GetTokenSubcommandTests
{
    private readonly ILogger _mockLogger;
    private readonly IConfigService _mockConfigService;
    private readonly AuthenticationService _mockAuthService;

    public GetTokenSubcommandTests()
    {
        _mockLogger = Substitute.For<ILogger>();
        _mockConfigService = Substitute.For<IConfigService>();
        _mockAuthService = Substitute.For<AuthenticationService>(Substitute.For<ILogger<AuthenticationService>>());
    }

    #region Command Structure Tests

    [Fact]
    public void CreateCommand_ShouldHaveCorrectName()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        command.Name.Should().Be("get-token");
    }

    [Fact]
    public void CreateCommand_ShouldHaveDescriptiveMessage()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        command.Description.Should().Contain("bearer token");
        command.Description.Should().Contain("MCP");
    }

    [Fact]
    public void CreateCommand_ShouldHaveConfigOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var configOption = command.Options.FirstOrDefault(o => o.Name == "config");
        configOption.Should().NotBeNull();
        configOption!.Aliases.Should().Contain("--config");
        configOption.Aliases.Should().Contain("-c");
    }

    [Fact]
    public void CreateCommand_ShouldHaveAppIdOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var appIdOption = command.Options.FirstOrDefault(o => o.Name == "app-id");
        appIdOption.Should().NotBeNull();
        appIdOption!.Aliases.Should().Contain("--app-id");
    }

    [Fact]
    public void CreateCommand_ShouldHaveManifestOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var manifestOption = command.Options.FirstOrDefault(o => o.Name == "manifest");
        manifestOption.Should().NotBeNull();
        manifestOption!.Aliases.Should().Contain("--manifest");
        manifestOption.Aliases.Should().Contain("-m");
    }

    [Fact]
    public void CreateCommand_ShouldHaveScopesOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var scopesOption = command.Options.FirstOrDefault(o => o.Name == "scopes");
        scopesOption.Should().NotBeNull();
        scopesOption!.Aliases.Should().Contain("--scopes");
    }

    [Fact]
    public void CreateCommand_ShouldHaveOutputOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var outputOption = command.Options.FirstOrDefault(o => o.Name == "output");
        outputOption.Should().NotBeNull();
        outputOption!.Aliases.Should().Contain("--output");
        outputOption.Aliases.Should().Contain("-o");
    }

    [Fact]
    public void CreateCommand_ShouldHaveVerboseOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var verboseOption = command.Options.FirstOrDefault(o => o.Name == "verbose");
        verboseOption.Should().NotBeNull();
        verboseOption!.Aliases.Should().Contain("--verbose");
        verboseOption.Aliases.Should().Contain("-v");
    }

    [Fact]
    public void CreateCommand_ShouldHaveForceRefreshOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var forceRefreshOption = command.Options.FirstOrDefault(o => o.Name == "force-refresh");
        forceRefreshOption.Should().NotBeNull();
        forceRefreshOption!.Aliases.Should().Contain("--force-refresh");
    }

    [Fact]
    public void CreateCommand_ShouldHaveResourceOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceOption = command.Options.FirstOrDefault(o => o.Name == "resource");
        resourceOption.Should().NotBeNull();
        resourceOption!.Aliases.Should().Contain("--resource");
    }

    [Fact]
    public void CreateCommand_ShouldHaveResourceIdOption()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceIdOption = command.Options.FirstOrDefault(o => o.Name == "resource-id");
        resourceIdOption.Should().NotBeNull();
        resourceIdOption!.Aliases.Should().Contain("--resource-id");
    }

    [Fact]
    public void CreateCommand_ShouldHaveAllRequiredOptions()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        command.Options.Should().HaveCount(9);
        var optionNames = command.Options.Select(opt => opt.Name).ToList();
        optionNames.Should().Contain(new[]
        {
            "config",
            "app-id",
            "manifest",
            "scopes",
            "output",
            "verbose",
            "force-refresh",
            "resource",
            "resource-id"
        });
    }

    #endregion

    #region Resource Option Tests

    [Fact]
    public void CreateCommand_ResourceOptionDescription_ShouldListAvailableKeywords()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceOption = command.Options.FirstOrDefault(o => o.Name == "resource");
        resourceOption.Should().NotBeNull();
        resourceOption!.Description.Should().Contain("mcp");
        resourceOption.Description.Should().Contain("powerplatform");
    }

    [Fact]
    public void CreateCommand_ResourceIdOptionDescription_ShouldMentionGuid()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceIdOption = command.Options.FirstOrDefault(o => o.Name == "resource-id");
        resourceIdOption.Should().NotBeNull();
        resourceIdOption!.Description.Should().Contain("GUID");
    }

    [Fact]
    public void CreateCommand_ResourceOption_ShouldNotBeRequired()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceOption = command.Options.FirstOrDefault(o => o.Name == "resource");
        resourceOption.Should().NotBeNull();
        resourceOption!.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void CreateCommand_ResourceIdOption_ShouldNotBeRequired()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceIdOption = command.Options.FirstOrDefault(o => o.Name == "resource-id");
        resourceIdOption.Should().NotBeNull();
        resourceIdOption!.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void CreateCommand_ResourceOptionDescription_ShouldIndicateScopesRequired()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceOption = command.Options.FirstOrDefault(o => o.Name == "resource");
        resourceOption.Should().NotBeNull();
        resourceOption!.Description.Should().Contain("--scopes");
    }

    [Fact]
    public void CreateCommand_ResourceIdOptionDescription_ShouldIndicateScopesRequired()
    {
        // Act
        var command = GetTokenSubcommand.CreateCommand(_mockLogger, _mockConfigService, _mockAuthService);

        // Assert
        var resourceIdOption = command.Options.FirstOrDefault(o => o.Name == "resource-id");
        resourceIdOption.Should().NotBeNull();
        resourceIdOption!.Description.Should().Contain("--scopes");
    }

    #endregion
}
