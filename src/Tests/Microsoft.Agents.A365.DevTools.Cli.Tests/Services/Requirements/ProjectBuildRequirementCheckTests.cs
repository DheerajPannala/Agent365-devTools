// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class ProjectBuildRequirementCheckTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly PlatformDetector _platformDetector;
    private readonly CommandExecutor _commandExecutor;
    private readonly ProjectBuildRequirementCheck _check;
    private readonly string _tempDir;

    public ProjectBuildRequirementCheckTests()
    {
        _logger = Substitute.For<ILogger>();
        _platformDetector = new PlatformDetector(Substitute.For<ILogger<PlatformDetector>>());
        _commandExecutor = Substitute.For<CommandExecutor>(Substitute.For<ILogger<CommandExecutor>>());
        _check = new ProjectBuildRequirementCheck(_platformDetector, _commandExecutor);
        _tempDir = Path.Combine(Path.GetTempPath(), $"a365-build-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Check_HasExpectedMetadata()
    {
        _check.Name.Should().Be("Project Build");
        _check.Category.Should().Be("Code Health");
        _check.Description.Should().Contain("warnings treated as errors");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckAsync_WhenDeploymentProjectPathIsEmpty_FallsBackToCwd(string? path)
    {
        // Arrange - when deploymentProjectPath is empty, falls back to CWD
        var config = new Agent365Config { DeploymentProjectPath = path ?? string.Empty };

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert - should not crash; CWD may or may not have a recognized project
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenDeploymentProjectPathIsInvalid_ReturnsFailure()
    {
        // Arrange
        var config = new Agent365Config { DeploymentProjectPath = "path\0with\0nulls" };

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert - Path.GetFullPath throws, caught by ExecuteCheckWithLoggingAsync
        result.Passed.Should().BeFalse(because: "an invalid path format should be reported as a failure");
    }

    [Fact]
    public async Task CheckAsync_WhenDeploymentProjectPathDoesNotExist_ReturnsFailure()
    {
        // Arrange
        var config = new Agent365Config { DeploymentProjectPath = Path.Combine(_tempDir, "nonexistent") };

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "a non-existent directory cannot be built");
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task CheckAsync_WhenPlatformIsUnknown_ReturnsWarning()
    {
        // Arrange - empty directory has no project files, so PlatformDetector returns Unknown
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "unknown platform is a non-blocking warning");
        result.IsWarning.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Could not detect project platform");
    }

    [Fact]
    public async Task CheckAsync_WhenDotNetBuildSucceeds_ReturnsSuccess()
    {
        // Arrange - create a .csproj so PlatformDetector identifies DotNet
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("dotnet"),
            Arg.Is<string>(a => a.Contains("TreatWarningsAsErrors")),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult { ExitCode = 0, StandardOutput = "Build succeeded." });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "a successful build should pass the check");
        result.Details.Should().Contain("DotNet");
    }

    [Fact]
    public async Task CheckAsync_WhenDotNetBuildFails_ReturnsFailure()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("dotnet"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardOutput = "Program.cs(10,5): error CS1002: ; expected\nBuild FAILED."
            });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "a failed build must be reported as a check failure");
        result.ErrorMessage.Should().Contain("build failed", because: "the error message should identify the failure type");
    }

    [Fact]
    public async Task CheckAsync_WhenDotNetBuildHasWarningsTreatedAsErrors_ReturnsFailure()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("dotnet"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardOutput = "Program.cs(5,1): error CS8600: Converting null literal or possible null value to non-nullable type. [Treated as error]\nBuild FAILED."
            });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "warnings treated as errors should cause build failure");
        result.ErrorMessage.Should().Contain("DotNet");
        result.ResolutionGuidance.Should().Contain("TreatWarningsAsErrors", because: "guidance should tell the user how to reproduce locally");
    }

    [Fact]
    public async Task CheckAsync_WhenNodeJsBuildSucceeds_ReturnsSuccess()
    {
        // Arrange - create a package.json so PlatformDetector identifies NodeJs
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("npm"),
            Arg.Is("run build"),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult { ExitCode = 0, StandardOutput = "Build completed." });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "a successful Node.js build should pass");
        result.Details.Should().Contain("NodeJs");
    }

    [Fact]
    public async Task CheckAsync_WhenNodeJsBuildFails_ReturnsFailure()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("npm"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardError = "Error: Cannot find module './config'"
            });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "a failed Node.js build must be reported");
        result.ResolutionGuidance.Should().Contain("npm run build");
    }

    [Fact]
    public async Task CheckAsync_WhenPythonBuildSucceeds_ReturnsSuccess()
    {
        // Arrange - create a .py file so PlatformDetector identifies Python
        File.WriteAllText(Path.Combine(_tempDir, "app.py"), "print('hello')");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Is("python"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult { ExitCode = 0, StandardOutput = "" });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "a successful Python syntax check should pass");
        result.Details.Should().Contain("Python");
    }

    [Fact]
    public async Task CheckAsync_WhenBuildFailsWithNoOutput_ReportsExitCode()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult { ExitCode = 1, StandardOutput = "", StandardError = "" });

        // Act
        var result = await _check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exited with code 1", because: "when there is no output, the exit code is the only diagnostic");
    }

    [Fact]
    public async Task CheckAsync_DotNetBuild_PassesTreatWarningsAsErrors()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };
        _commandExecutor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns(new CommandResult { ExitCode = 0 });

        // Act
        await _check.CheckAsync(config, _logger);

        // Assert - verify the correct build arguments were passed
        await _commandExecutor.Received(1).ExecuteAsync(
            "dotnet",
            Arg.Is<string>(a => a.Contains("/p:TreatWarningsAsErrors=true")),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }
}
