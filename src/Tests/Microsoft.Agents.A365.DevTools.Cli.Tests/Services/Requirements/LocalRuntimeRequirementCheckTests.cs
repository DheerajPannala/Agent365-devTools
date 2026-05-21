// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class LocalRuntimeRequirementCheckTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly PlatformDetector _platformDetector;
    private readonly IProcessService _processService;
    private readonly string _tempDir;

    public LocalRuntimeRequirementCheckTests()
    {
        _logger = Substitute.For<ILogger>();
        _platformDetector = new PlatformDetector(Substitute.For<ILogger<PlatformDetector>>());
        _processService = Substitute.For<IProcessService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"a365-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private LocalRuntimeRequirementCheck CreateCheck(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null ? new HttpClient(handler) : new HttpClient();
        return new LocalRuntimeRequirementCheck(_platformDetector, _processService, httpClient);
    }

    [Fact]
    public void Check_HasExpectedMetadata()
    {
        var check = CreateCheck();
        check.Name.Should().Be("Local Runtime");
        check.Category.Should().Be("Code Health");
        check.Description.Should().Contain("health endpoint");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckAsync_WhenDeploymentProjectPathIsEmpty_FallsBackToCwd(string? path)
    {
        // Arrange - when deploymentProjectPath is empty, falls back to CWD
        var check = CreateCheck();
        var config = new Agent365Config { DeploymentProjectPath = path ?? string.Empty };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert - should not crash; CWD may or may not have a recognized project
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenDeploymentProjectPathIsInvalid_ReturnsFailure()
    {
        // Arrange
        var check = CreateCheck();
        var config = new Agent365Config { DeploymentProjectPath = "path\0with\0nulls" };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert - Path.GetFullPath throws, caught by ExecuteCheckWithLoggingAsync
        result.Passed.Should().BeFalse(because: "an invalid path format should be reported as a failure");
    }

    [Fact]
    public async Task CheckAsync_WhenDirectoryDoesNotExist_ReturnsFailure()
    {
        // Arrange
        var check = CreateCheck();
        var config = new Agent365Config { DeploymentProjectPath = Path.Combine(_tempDir, "nonexistent") };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "a non-existent directory cannot run an app");
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task CheckAsync_WhenPlatformIsUnknown_ReturnsWarning()
    {
        // Arrange - empty directory, PlatformDetector returns Unknown
        var check = CreateCheck();
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "unknown platform is a non-blocking warning");
        result.IsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenProcessFailsToStart_ReturnsFailure()
    {
        // Arrange - create a .csproj so platform is detected as DotNet
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        _processService.Start(Arg.Any<ProcessStartInfo>()).Returns((Process?)null);
        var check = CreateCheck();
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "a process that fails to start cannot serve health requests");
        result.ErrorMessage.Should().Contain("Failed to start");
    }

    [Fact]
    public async Task CheckAsync_WhenHealthEndpointResponds200_ReturnsSuccess()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var fakeProcess = CreateFakeProcess(exitImmediately: false);
        _processService.Start(Arg.Any<ProcessStartInfo>()).Returns(fakeProcess);

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var check = CreateCheck(handler);
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue(because: "a health endpoint returning 200 means the app is running");
        result.Details.Should().Contain("200");
    }

    [Fact]
    public async Task CheckAsync_WhenProcessExitsEarly_ReturnsFailure()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var fakeProcess = CreateFakeProcess(exitImmediately: true, exitCode: 1);
        _processService.Start(Arg.Any<ProcessStartInfo>()).Returns(fakeProcess);

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var check = CreateCheck(handler);
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeFalse(because: "an early exit means the app crashed before responding");
        result.ErrorMessage.Should().Contain("exited early");
    }

    [Theory]
    [InlineData("https://localhost:3978/api/messages", 3978)]
    [InlineData("https://127.0.0.1:8080/api/messages", 8080)]
    [InlineData("https://myapp.azurewebsites.net/api/messages", 5000)]
    [InlineData("https://localhost/api/messages", 5000)]
    [InlineData("", 5000)]
    [InlineData(null, 5000)]
    public void ResolvePort_ReturnsExpectedPort(string? endpoint, int expected)
    {
        var port = LocalRuntimeRequirementCheck.ResolvePort(endpoint);
        port.Should().Be(expected, because: "port resolution should respect localhost URLs and fall back to default");
    }

    [Fact]
    public async Task CheckAsync_NodeJsProject_UsesNpmStart()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        var fakeProcess = CreateFakeProcess(exitImmediately: false);
        _processService.Start(Arg.Any<ProcessStartInfo>()).Returns(fakeProcess);

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var check = CreateCheck(handler);
        var config = new Agent365Config { DeploymentProjectPath = _tempDir };

        // Act
        var result = await check.CheckAsync(config, _logger);

        // Assert
        result.Passed.Should().BeTrue();
        _processService.Received(1).Start(Arg.Is<ProcessStartInfo>(p =>
            p.FileName == "npm" && p.Arguments == "start"));
    }

    [Fact]
    public async Task CheckAsync_DotNetProject_SetsAspNetCoreUrls()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project />");
        var fakeProcess = CreateFakeProcess(exitImmediately: false);
        _processService.Start(Arg.Any<ProcessStartInfo>()).Returns(fakeProcess);

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var check = CreateCheck(handler);
        var config = new Agent365Config
        {
            DeploymentProjectPath = _tempDir,
            MessagingEndpoint = "https://localhost:3978/api/messages"
        };

        // Act
        await check.CheckAsync(config, _logger);

        // Assert
        _processService.Received(1).Start(Arg.Is<ProcessStartInfo>(p =>
            p.FileName == "dotnet" &&
            p.EnvironmentVariables["ASPNETCORE_URLS"] == "http://localhost:3978"));
    }

    /// <summary>
    /// Creates a fake Process for testing. When exitImmediately is true, the process
    /// appears to have already exited.
    /// </summary>
    private static Process CreateFakeProcess(bool exitImmediately, int exitCode = 0)
    {
        // Start a real but trivial process we can control
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? (exitImmediately ? "/c exit 1" : "/c ping -n 60 127.0.0.1 >nul")
                : (exitImmediately ? "-c 'exit 1'" : "-c 'sleep 60'"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)!;

        if (exitImmediately)
        {
            process.WaitForExit(5000);
        }

        return process;
    }

    /// <summary>
    /// Fake HTTP handler that returns a configurable status code.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public FakeHttpHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}
