// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

public class NodeBuilderTests : IDisposable
{
    private readonly ILogger<NodeBuilder> _logger;
    private readonly NodeBuilder _builder;
    private readonly List<string> _tempDirectories;

    public NodeBuilderTests()
    {
        _logger = Substitute.For<ILogger<NodeBuilder>>();
        var executorLogger = Substitute.For<ILogger<CommandExecutor>>();
        var mockExecutor = Substitute.ForPartsOf<CommandExecutor>(executorLogger);
        _builder = new NodeBuilder(_logger, mockExecutor);
        _tempDirectories = new List<string>();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateManifestAsync_TypeScriptProjectWithDistFolder_SkipsOryxBuild()
    {
        // Arrange
        var projectDir = CreateTempDirectory();
        var publishPath = CreateTempDirectory();

        WritePackageJson(projectDir, buildScript: "tsc", startScript: "node dist/index.js");
        File.WriteAllText(Path.Combine(projectDir, "tsconfig.json"), "{}");
        Directory.CreateDirectory(Path.Combine(publishPath, "dist"));

        // Act
        var manifest = await _builder.CreateManifestAsync(projectDir, publishPath);

        // Assert
        manifest.BuildRequired.Should().BeFalse(
            because: "when dist/ exists and tsconfig.json is present, TypeScript was compiled locally " +
                     "and Oryx must not re-run npm run build — Oryx's production install skips devDependencies " +
                     "so tsc would not be found, causing deployment failure");
        manifest.BuildCommand.Should().BeEmpty(
            because: "no build command should be set when the Oryx remote build is skipped");
    }

    [Fact]
    public async Task CreateManifestAsync_TypeScriptProjectWithoutDistFolder_UsesOryxBuild()
    {
        // Arrange
        var projectDir = CreateTempDirectory();
        var publishPath = CreateTempDirectory();

        WritePackageJson(projectDir, buildScript: "tsc", startScript: "node dist/index.js");
        File.WriteAllText(Path.Combine(projectDir, "tsconfig.json"), "{}");
        // No dist/ in publish output — TypeScript not yet compiled

        // Act
        var manifest = await _builder.CreateManifestAsync(projectDir, publishPath);

        // Assert
        manifest.BuildRequired.Should().BeTrue(
            because: "when dist/ is absent the TypeScript project was not pre-compiled so Oryx must run npm run build");
        manifest.BuildCommand.Should().Be("npm run build");
    }

    [Fact]
    public async Task CreateManifestAsync_JavaScriptProjectWithDistFolder_UsesOryxBuild()
    {
        // Arrange
        var projectDir = CreateTempDirectory();
        var publishPath = CreateTempDirectory();

        WritePackageJson(projectDir, buildScript: "webpack", startScript: "node dist/bundle.js");
        // No tsconfig.json — JavaScript-only project with webpack producing dist/
        Directory.CreateDirectory(Path.Combine(publishPath, "dist"));

        // Act
        var manifest = await _builder.CreateManifestAsync(projectDir, publishPath);

        // Assert
        manifest.BuildRequired.Should().BeTrue(
            because: "JavaScript-only projects without tsconfig.json should still use Oryx remote build " +
                     "even when dist/ exists — skipping would be incorrect since the build script produces the bundle");
        manifest.BuildCommand.Should().Be("npm run build");
    }

    [Fact]
    public async Task CreateManifestAsync_WithoutBuildScript_DoesNotSetBuildRequired()
    {
        // Arrange
        var projectDir = CreateTempDirectory();
        var publishPath = CreateTempDirectory();

        WritePackageJson(projectDir, buildScript: null, startScript: "node server.js");

        // Act
        var manifest = await _builder.CreateManifestAsync(projectDir, publishPath);

        // Assert
        manifest.BuildRequired.Should().BeFalse(
            because: "no build script in package.json means Oryx only runs npm install, not a build step");
        manifest.BuildCommand.Should().BeEmpty();
    }

    private static void WritePackageJson(string projectDir, string? buildScript, string startScript)
    {
        var scripts = buildScript is not null
            ? $@"""build"": ""{buildScript}"", ""start"": ""{startScript}"""
            : $@"""start"": ""{startScript}""";

        File.WriteAllText(Path.Combine(projectDir, "package.json"), $$"""
            {
                "scripts": {
                    {{scripts}}
                }
            }
            """);
    }

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirectories.Add(dir);
        return dir;
    }
}
