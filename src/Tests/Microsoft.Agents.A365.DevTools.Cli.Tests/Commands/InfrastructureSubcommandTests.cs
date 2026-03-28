// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Commands.SetupSubcommands;
using Microsoft.Agents.A365.DevTools.Cli.Exceptions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Commands;

public class InfrastructureSubcommandTests
{
    private readonly ILogger _logger;
    private readonly CommandExecutor _commandExecutor;

    public InfrastructureSubcommandTests()
    {
        _logger = Substitute.For<ILogger>();
        _commandExecutor = Substitute.For<CommandExecutor>(Substitute.For<ILogger<CommandExecutor>>());
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WhenQuotaLimitExceeded_ThrowsInvalidOperationException()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        // Mock app service plan doesn't exist (initial check)
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        // Real az CLI output from `az appservice plan create --sku B1` when quota is exceeded.
        // Note: az CLI does NOT include an ARM error code for this error — it outputs plain English text.
        // This test exercises the English text fallback path (matching "quota").
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardError = "ERROR: Operation cannot be completed without additional quota.\n\nAdditional details - Location:\n\nCurrent Limit (Basic VMs): 0\n\nCurrent Usage: 0\n\nAmount required for this deployment (Basic VMs): 1"
            });

        // Act & Assert - The method should throw immediately because creation fails
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.QuotaExceeded);
        exception.PlanName.Should().Be(planName);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WhenPlanAlreadyExists_SkipsCreation()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "existing-plan";
        var planSku = "B1";

        // Mock app service plan already exists
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult
            {
                ExitCode = 0,
                StandardOutput = "{\"name\": \"existing-plan\", \"sku\": {\"name\": \"B1\"}}"
            });

        // Act
        await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
            _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
            maxRetries: 2, baseDelaySeconds: 0);

        // Assert - Verify creation command was never called
        await _commandExecutor.DidNotReceive().ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create")),
            captureOutput: true,
            suppressErrorLogging: true);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WhenCreationSucceeds_VerifiesExistence()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "new-plan";
        var planSku = "B1";

        // Mock app service plan doesn't exist initially, then exists after creation
        var planShowCallCount = 0;
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(callInfo =>
            {
                planShowCallCount++;
                // First call: plan doesn't exist, second call (after creation): plan exists
                return planShowCallCount == 1
                    ? new CommandResult { ExitCode = 1, StandardError = "Plan not found" }
                    : new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"new-plan\"}" };
            });

        // Mock app service plan creation succeeds
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 0, StandardOutput = "Plan created" });

        // Act
        await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
            _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
            maxRetries: 2, baseDelaySeconds: 0);

        // Assert - Verify the plan creation was called
        await _commandExecutor.Received(1).ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true);

        // Verify the plan was checked twice (before creation and verification after)
        await _commandExecutor.Received(2).ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WhenCreationFailsSilently_ThrowsInvalidOperationException()
    {
        // Arrange - Tests the scenario where Azure CLI returns success but the plan doesn't actually exist
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "failed-plan";
        var planSku = "B1";

        // Mock app service plan doesn't exist before and after creation attempt
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        // Mock plan creation appears to succeed but doesn't actually create the plan
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 0, StandardOutput = "" });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.VerificationTimeout);
        exception.PlanName.Should().Be(planName);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WhenPermissionDenied_ThrowsInvalidOperationException()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        // Mock app service plan doesn't exist
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        // Mock app service plan creation fails with permission error (using ARM error code, locale-independent)
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardError = "ERROR: (AuthorizationFailed) The client does not have authorization to perform action"
            });

        // Act & Assert - The method should throw immediately because creation fails
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.AuthorizationFailed);
        exception.PlanName.Should().Be(planName);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WithRetry_WhenPlanPropagatesSlowly_EventuallySucceeds()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "slow-plan";
        var planSku = "B1";

        // Mock app service plan doesn't exist initially
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName) && !s.Contains("create")),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(
                new CommandResult { ExitCode = 1, StandardError = "Plan not found" },
                new CommandResult { ExitCode = 1, StandardError = "Plan not found" },
                new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"slow-plan\"}" });

        // Mock app service plan creation succeeds
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 0 });

        // Act
        await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
            _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
            maxRetries: 2, baseDelaySeconds: 0);

        // Assert - Verify show was called multiple times (initial check + retries)
        await _commandExecutor.Received(3).ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WithRetry_WhenPlanNeverAppears_ThrowsAfterRetries()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "missing-plan";
        var planSku = "B1";

        // Mock app service plan never appears even after creation
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        // Mock app service plan creation succeeds
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 0 });

        // Act & Assert - Use minimal retries for test performance
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, 
                _logger, 
                resourceGroup, 
                planName, 
                planSku, 
                "eastus",
                subscriptionId,
                maxRetries: 2,
                baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.VerificationTimeout);
        exception.PlanName.Should().Be(planName);
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenUserIdAvailable_AssignsWebsiteContributorRole()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var tenantId = "test-tenant-id";
        var resourceGroup = "test-rg";
        var location = "eastus";
        var planName = "test-plan";
        var webAppName = "test-webapp";
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = Substitute.For<ILogger>();
        
        try
        {
            // Create temporary project directory
            Directory.CreateDirectory(deploymentProjectPath);
            
            // Setup mock CommandExecutor to return success for all operations
            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);

                    // Resource group exists check
                    if (args.Contains("group exists"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "true" };

                    // App service plan show
                    if (args.Contains("appservice plan show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };

                    // Web app show - succeeds after creation to avoid retry timeout
                    if (args.Contains("webapp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };

                    // Web app create
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\"}" };

                    // Managed identity assign
                    if (args.Contains("webapp identity assign"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };

                    // MSI verification
                    if (args.Contains("ad sp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };

                    // Get current user object ID - Use valid GUID format
                    if (args.Contains("ad signed-in-user show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "12345678-1234-1234-1234-123456789abc" };

                    // Role pre-check: no existing role found (empty output triggers assignment)
                    if (args.Contains("role assignment list"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "" };

                    // Role assignment create
                    if (args.Contains("role assignment create"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-role-assignment-id\"}" };

                    return new CommandResult { ExitCode = 0 };
                });

            // Act
            (string? principalId, bool anyAlreadyExisted) = await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor,
                subscriptionId,
                tenantId,
                resourceGroup,
                location,
                planName,
                "B1",
                webAppName,
                generatedConfigPath,
                deploymentProjectPath,
                ProjectPlatform.DotNet,
                logger,
                needDeployment: true,
                skipInfra: false,
                externalHosting: false,
                CancellationToken.None);

            // Assert - Verify pre-check was called (role assignment list with include-inherited)
            await _commandExecutor.Received().ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("role assignment list") && s.Contains("include-inherited")),
                captureOutput: true,
                suppressErrorLogging: true);

            // Assert - Verify role assignment create was called (since pre-check returned empty)
            await _commandExecutor.Received().ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("role assignment create") && s.Contains("Website Contributor")),
                captureOutput: true,
                suppressErrorLogging: true);
        }
        finally
        {
            // Cleanup
            if (File.Exists(generatedConfigPath))
                File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath))
                Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenUserIdUnavailable_ContinuesWithoutRoleAssignment()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var tenantId = "test-tenant-id";
        var resourceGroup = "test-rg";
        var location = "eastus";
        var planName = "test-plan";
        var webAppName = "test-webapp";
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = Substitute.For<ILogger>();
        
        try
        {
            // Create temporary project directory
            Directory.CreateDirectory(deploymentProjectPath);
            
            // Setup mock CommandExecutor
            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);

                    // Resource group exists check
                    if (args.Contains("group exists"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "true" };

                    // App service plan show
                    if (args.Contains("appservice plan show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };

                    // Web app show - succeeds after creation to avoid retry timeout
                    if (args.Contains("webapp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };

                    // Web app create
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\"}" };

                    // Managed identity assign
                    if (args.Contains("webapp identity assign"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };

                    // MSI verification
                    if (args.Contains("ad sp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };

                    // Get current user object ID - fails (service principal scenario)
                    if (args.Contains("ad signed-in-user show"))
                        return new CommandResult { ExitCode = 1, StandardError = "Not logged in as user" };

                    return new CommandResult { ExitCode = 0 };
                });

            // Act - Should not throw, just log a debug message
            (string? principalId, bool anyAlreadyExisted) = await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor,
                subscriptionId,
                tenantId,
                resourceGroup,
                location,
                planName,
                "B1",
                webAppName,
                generatedConfigPath,
                deploymentProjectPath,
                ProjectPlatform.DotNet,
                logger,
                needDeployment: true,
                skipInfra: false,
                externalHosting: false,
                CancellationToken.None);

            // Assert - Principal ID should still be set, role assignment just skipped
            principalId.Should().Be("test-principal-id");
            
            // Verify role assignment was NOT attempted (since user ID retrieval failed)
            await _commandExecutor.DidNotReceive().ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("role assignment create")),
                captureOutput: true,
                suppressErrorLogging: true);
        }
        finally
        {
            // Cleanup
            if (File.Exists(generatedConfigPath))
                File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath))
                Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenRoleAssignmentFails_ContinuesWithWarning()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var tenantId = "test-tenant-id";
        var resourceGroup = "test-rg";
        var location = "eastus";
        var planName = "test-plan";
        var webAppName = "test-webapp";
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            // Create temporary project directory
            Directory.CreateDirectory(deploymentProjectPath);
            
            // Setup mock CommandExecutor
            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    
                    // Resource group exists check
                    if (args.Contains("group exists"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "true" };
                    
                    // App service plan show
                    if (args.Contains("appservice plan show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };

                    // Web app show - doesn't exist initially, then exists after creation
                    if (args.Contains("webapp show"))
                    {
                        // Return success after creation to avoid retry timeout
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };
                    }

                    // Web app create
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\"}" };

                    // Managed identity assign
                    if (args.Contains("webapp identity assign"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };

                    // MSI verification
                    if (args.Contains("ad sp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };

                    // Get current user object ID - Use valid GUID format
                    if (args.Contains("ad signed-in-user show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "12345678-1234-1234-1234-123456789abc" };

                    // Role assignment - fails with permission error
                    if (args.Contains("role assignment create"))
                        return new CommandResult { ExitCode = 1, StandardError = "Insufficient permissions" };

                    // Role assignment verification - succeeds but returns empty (no role found)
                    if (args.Contains("role assignment list"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "" };

                    return new CommandResult { ExitCode = 0 };
                });

            // Act - Should not throw, just log a warning
            (string? principalId, bool anyAlreadyExisted) = await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor,
                subscriptionId,
                tenantId,
                resourceGroup,
                location,
                planName,
                "B1",
                webAppName,
                generatedConfigPath,
                deploymentProjectPath,
                ProjectPlatform.DotNet,
                logger,
                needDeployment: true,
                skipInfra: false,
                externalHosting: false,
                CancellationToken.None);

            // Assert - Principal ID should still be set, warning logged
            principalId.Should().Be("test-principal-id");
            logger.HasWarning("Could not assign Website Contributor role to user. Diagnostic logs may not be accessible.")
                .Should().BeTrue("the code must warn when role assignment fails");
        }
        finally
        {
            // Cleanup
            if (File.Exists(generatedConfigPath))
                File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath))
                Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenRoleAlreadyExists_VerifiesSuccessfully()
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var tenantId = "test-tenant-id";
        var resourceGroup = "test-rg";
        var location = "eastus";
        var planName = "test-plan";
        var webAppName = "test-webapp";
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            // Create temporary project directory
            Directory.CreateDirectory(deploymentProjectPath);

            // Setup mock CommandExecutor
            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);

                    // Resource group exists check
                    if (args.Contains("group exists"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "true" };

                    // App service plan show
                    if (args.Contains("appservice plan show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };

                    // Web app show - succeeds after creation to avoid retry timeout
                    if (args.Contains("webapp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };

                    // Web app create
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\"}" };

                    // Managed identity assign
                    if (args.Contains("webapp identity assign"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };

                    // MSI verification
                    if (args.Contains("ad sp show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };

                    // Get current user object ID - Use valid GUID format
                    if (args.Contains("ad signed-in-user show"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "12345678-1234-1234-1234-123456789abc" };

                    // Role assignment - already exists
                    if (args.Contains("role assignment create"))
                        return new CommandResult { ExitCode = 1, StandardError = "Role assignment already exists for this principal" };

                    // Role assignment verification - succeeds because it already exists
                    if (args.Contains("role assignment list"))
                        return new CommandResult { ExitCode = 0, StandardOutput = "Website Contributor" };

                    return new CommandResult { ExitCode = 0 };
                });

            // Act
            (string? principalId, bool anyAlreadyExisted) = await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor,
                subscriptionId,
                tenantId,
                resourceGroup,
                location,
                planName,
                "B1",
                webAppName,
                generatedConfigPath,
                deploymentProjectPath,
                ProjectPlatform.DotNet,
                logger,
                needDeployment: true,
                skipInfra: false,
                externalHosting: false,
                CancellationToken.None);

            // Assert - Principal ID should be set
            principalId.Should().Be("test-principal-id");

            // Verify pre-check (role assignment list --include-inherited) was called
            await _commandExecutor.Received().ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("role assignment list") && s.Contains("include-inherited")),
                captureOutput: true,
                suppressErrorLogging: true);

            logger.HasInformation("log access confirmed, skipping")
                .Should().BeTrue("the code must log when an existing role is detected and assignment is skipped");
        }
        finally
        {
            // Cleanup
            if (File.Exists(generatedConfigPath))
                File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath))
                Directory.Delete(deploymentProjectPath, true);
        }
    }

    #region Locale-Independent Error Code Tests (GitHub Issue #100)

    [Theory]
    [InlineData("ERROR: (AuthorizationFailed) Der Client hat keine Berechtigung", "because ARM error code AuthorizationFailed is present regardless of German locale")]
    [InlineData("ERROR: (AuthorizationFailed) Le client n'a pas l'autorisation", "because ARM error code AuthorizationFailed is present regardless of French locale")]
    [InlineData("ERROR: (LinkedAuthorizationFailed) Authorization failed", "because LinkedAuthorizationFailed is a recognized ARM error code")]
    [InlineData("ERROR: (AuthorizationFailed) The client 'user@contoso.onmicrosoft.com' with object id 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' does not have authorization to perform action 'Microsoft.Web/serverfarms/write' over scope '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Web/serverfarms/plan-test' or the scope is invalid.", "because real az CLI AuthorizationFailed output includes the ARM error code in parentheses")]
    public async Task EnsureAppServicePlanExists_WithNonEnglishLocale_AuthorizationFailed_DetectsErrorCode(string stderr, string because)
    {
        // Arrange - Simulates Azure CLI error output in non-English locales.
        // ARM error codes like AuthorizationFailed are never localized.
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = stderr });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.AuthorizationFailed, because);
    }

    [Theory]
    [InlineData("ERROR: (QuotaExceeded) Kontingent ueberschritten", "because ARM error code QuotaExceeded is present regardless of German locale")]
    [InlineData("ERROR: (ResourceQuotaExceeded) Quota exceeded", "because ResourceQuotaExceeded is a recognized ARM error code")]
    [InlineData("ERROR: Operation cannot be completed without additional quota.\n\nAdditional details - Location:\n\nCurrent Limit (Basic VMs): 0\n\nCurrent Usage: 0\n\nAmount required for this deployment (Basic VMs): 1", "because real az CLI quota errors have no ARM error code but contain the word 'quota' as English text fallback")]
    public async Task EnsureAppServicePlanExists_WithNonEnglishLocale_QuotaExceeded_DetectsErrorCode(string stderr, string because)
    {
        // Arrange - Simulates Azure CLI error output where only the error code
        // (not the localized message) should be matched.
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = stderr });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.QuotaExceeded, because);
    }

    [Theory]
    [InlineData("ERROR: (InvalidSku) Die SKU ist ungueltig", "because ARM error code InvalidSku is present regardless of locale")]
    [InlineData("ERROR: (SkuNotAvailable) SKU no disponible", "because ARM error code SkuNotAvailable is present regardless of Spanish locale")]
    [InlineData("ERROR: az appservice plan create: 'FAKEINVALIDSKU' is not a valid value for '--sku'. Allowed values: F1, FREE, D1, SHARED, B1, B2, B3, S1, S2, S3, P1V2, P2V2, P3V2, P0V3, P1V3, P2V3, P3V3, P1MV3, P2MV3, P3MV3, P4MV3, P5MV3, I1, I2, I3, I1V2, I2V2, I3V2, I4V2, I5V2, I6V2, I1MV2, I2MV2, I3MV2, I4MV2, I5MV2, WS1, WS2, WS3.", "because real az CLI invalid SKU uses client-side validation text with no ARM error code — matches English text fallback")]
    public async Task EnsureAppServicePlanExists_WithNonEnglishLocale_SkuErrors_DetectsErrorCode(string stderr, string because)
    {
        // Arrange
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = stderr });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.SkuNotAvailable, because);
    }

    [Fact]
    public async Task EnsureAppServicePlanExists_WithOnlyLocalizedErrorMessage_FallsThrough()
    {
        // Arrange - When stderr contains ONLY localized text without an ARM error code,
        // the error should fall through to the generic 'Other' error type rather than
        // being incorrectly classified. This verifies we no longer match on localized text.
        var subscriptionId = "test-sub-id";
        var resourceGroup = "test-rg";
        var planName = "test-plan";
        var planSku = "B1";

        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan show") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult { ExitCode = 1, StandardError = "Plan not found" });

        // Error message in a non-English locale with NO ARM error code — just localized text
        _commandExecutor.ExecuteAsync("az",
            Arg.Is<string>(s => s.Contains("appservice plan create") && s.Contains(planName)),
            captureOutput: true,
            suppressErrorLogging: true)
            .Returns(new CommandResult
            {
                ExitCode = 1,
                StandardError = "Der Client hat keine Berechtigung diese Aktion auszufuehren"
            });

        // Act & Assert - Should fall through to 'Other' since there's no ARM error code
        var exception = await Assert.ThrowsAsync<AzureAppServicePlanException>(
            async () => await InfrastructureSubcommand.EnsureAppServicePlanExistsAsync(
                _commandExecutor, _logger, resourceGroup, planName, planSku, "eastus", subscriptionId,
                maxRetries: 2, baseDelaySeconds: 0));

        exception.ErrorType.Should().Be(AppServicePlanErrorType.Other,
            because: "without an ARM error code in the output, the error should not be classified as authorization/quota/sku");
    }

    #endregion

    #region CreateInfrastructureAsync Error Path Tests (GitHub Issue #100 - Coverage)

    [Fact]
    public async Task CreateInfrastructureAsync_WhenWebAppCreateFailsWithAuthorizationFailed_ThrowsAzureResourceException()
    {
        // Arrange - Exercises the ARM error code path for webapp create AuthorizationFailed.
        // Real az CLI output: "ERROR: (AuthorizationFailed) The client '...' does not have authorization..."
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            Directory.CreateDirectory(deploymentProjectPath);

            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    if (args.Contains("group exists")) return new CommandResult { ExitCode = 0, StandardOutput = "true" };
                    if (args.Contains("appservice plan show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };
                    if (args.Contains("webapp show")) return new CommandResult { ExitCode = 1, StandardError = "Not found" };
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 1, StandardError = "ERROR: (AuthorizationFailed) The client 'user@example.com' does not have authorization to perform action 'Microsoft.Web/sites/write'" };
                    return new CommandResult { ExitCode = 0 };
                });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<AzureResourceException>(
                async () => await InfrastructureSubcommand.CreateInfrastructureAsync(
                    _commandExecutor, "test-sub", "test-tenant", "test-rg", "eastus", "test-plan", "B1",
                    "test-webapp", generatedConfigPath, deploymentProjectPath, ProjectPlatform.DotNet, logger,
                    needDeployment: true, skipInfra: false, externalHosting: false, CancellationToken.None));

            exception.ErrorCode.Should().Be("AZURE_PERMISSION_DENIED",
                because: "AuthorizationFailed ARM error code indicates a permissions issue");
        }
        finally
        {
            if (File.Exists(generatedConfigPath)) File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath)) Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Theory]
    [InlineData("ERROR: (Conflict) Website with given name test already exists.", "because ARM Conflict error code is locale-independent")]
    [InlineData("WARNING: Webapp 'test-webapp' already exists. Returning the webapp's existing details.\nERROR: Unable to retrieve details of the existing app 'test-webapp'.", "because real az CLI uses 'already exists' English text without an ARM error code")]
    public async Task CreateInfrastructureAsync_WhenWebAppNameTaken_ThrowsAzureResourceExceptionWithNameTaken(string stderr, string because)
    {
        // Arrange - Tests both ARM error code and English text fallback paths for webapp name conflict.
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            Directory.CreateDirectory(deploymentProjectPath);

            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    if (args.Contains("group exists")) return new CommandResult { ExitCode = 0, StandardOutput = "true" };
                    if (args.Contains("appservice plan show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };
                    if (args.Contains("webapp show")) return new CommandResult { ExitCode = 1, StandardError = "Not found" };
                    if (args.Contains("webapp create"))
                        return new CommandResult { ExitCode = 1, StandardError = stderr };
                    return new CommandResult { ExitCode = 0 };
                });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<AzureResourceException>(
                async () => await InfrastructureSubcommand.CreateInfrastructureAsync(
                    _commandExecutor, "test-sub", "test-tenant", "test-rg", "eastus", "test-plan", "B1",
                    "test-webapp", generatedConfigPath, deploymentProjectPath, ProjectPlatform.DotNet, logger,
                    needDeployment: true, skipInfra: false, externalHosting: false, CancellationToken.None));

            exception.ErrorCode.Should().Be("AZURE_WEBAPP_NAME_TAKEN", because);
        }
        finally
        {
            if (File.Exists(generatedConfigPath)) File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath)) Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenIdentityConflict_LogsAndContinues()
    {
        // Arrange - Tests the identity conflict path when az webapp identity assign returns
        // a Conflict error (or English fallback text).
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            Directory.CreateDirectory(deploymentProjectPath);

            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    if (args.Contains("group exists")) return new CommandResult { ExitCode = 0, StandardOutput = "true" };
                    if (args.Contains("appservice plan show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };
                    if (args.Contains("webapp show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };
                    if (args.Contains("webapp identity assign"))
                        return new CommandResult { ExitCode = 1, StandardError = "ERROR: (Conflict) The resource already has a managed identity" };
                    if (args.Contains("ad signed-in-user show")) return new CommandResult { ExitCode = 1, StandardError = "Not available" };
                    return new CommandResult { ExitCode = 0 };
                });

            // Act - Should not throw; logs the conflict and continues
            (string? principalId, bool anyAlreadyExisted) = await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor, "test-sub", "test-tenant", "test-rg", "eastus", "test-plan", "B1",
                "test-webapp", generatedConfigPath, deploymentProjectPath, ProjectPlatform.DotNet, logger,
                needDeployment: true, skipInfra: false, externalHosting: false, CancellationToken.None);

            // Assert
            logger.HasInformation("Managed identity already assigned").Should().BeTrue(
                because: "Conflict errors for identity assign should be treated as already-assigned");
        }
        finally
        {
            if (File.Exists(generatedConfigPath)) File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath)) Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenResourceGroupCreateConflict_LogsAlreadyExists()
    {
        // Arrange - Tests AzWarnAsync Conflict path: when resource group creation returns
        // a Conflict error (already exists), it should log and continue.
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            Directory.CreateDirectory(deploymentProjectPath);

            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    if (args.Contains("group exists")) return new CommandResult { ExitCode = 0, StandardOutput = "false" };
                    if (args.Contains("appservice plan show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };
                    if (args.Contains("webapp show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };
                    if (args.Contains("webapp identity assign")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };
                    if (args.Contains("ad sp show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };
                    if (args.Contains("ad signed-in-user show")) return new CommandResult { ExitCode = 1, StandardError = "Not available" };
                    return new CommandResult { ExitCode = 0 };
                });

            // Override the AzWarnAsync call for group create to simulate Conflict.
            // AzWarnAsync calls ExecuteAsync("az", args, suppressErrorLogging: true) which uses
            // default captureOutput: true, so we match on all parameters.
            _commandExecutor.ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("group create")),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                true,
                Arg.Any<CancellationToken>())
                .Returns(new CommandResult { ExitCode = 1, StandardError = "ERROR: (Conflict) Resource group already exists" });

            // Act
            await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor, "test-sub", "test-tenant", "test-rg", "eastus", "test-plan", "B1",
                "test-webapp", generatedConfigPath, deploymentProjectPath, ProjectPlatform.DotNet, logger,
                needDeployment: true, skipInfra: false, externalHosting: false, CancellationToken.None);

            // Assert
            logger.HasInformation("already exists").Should().BeTrue(
                because: "AzWarnAsync should log 'already exists' when a Conflict error is returned");
        }
        finally
        {
            if (File.Exists(generatedConfigPath)) File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath)) Directory.Delete(deploymentProjectPath, true);
        }
    }

    [Fact]
    public async Task CreateInfrastructureAsync_WhenResourceGroupCreateAuthorizationFailed_HandlesPermissionError()
    {
        // Arrange - Tests AzWarnAsync AuthorizationFailed path: when resource group creation
        // fails with AuthorizationFailed, it should handle it as a permission error.
        var generatedConfigPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
        var deploymentProjectPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        var logger = new TestLogger();

        try
        {
            Directory.CreateDirectory(deploymentProjectPath);

            _commandExecutor.ExecuteAsync("az", Arg.Any<string>(), captureOutput: true, suppressErrorLogging: Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var args = callInfo.ArgAt<string>(1);
                    if (args.Contains("group exists")) return new CommandResult { ExitCode = 0, StandardOutput = "false" };
                    if (args.Contains("appservice plan show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-plan\"}" };
                    if (args.Contains("webapp show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"name\": \"test-webapp\", \"state\": \"Running\"}" };
                    if (args.Contains("webapp identity assign")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"principalId\": \"test-principal-id\"}" };
                    if (args.Contains("ad sp show")) return new CommandResult { ExitCode = 0, StandardOutput = "{\"id\": \"test-principal-id\"}" };
                    if (args.Contains("ad signed-in-user show")) return new CommandResult { ExitCode = 1, StandardError = "Not available" };
                    return new CommandResult { ExitCode = 0 };
                });

            // Override the AzWarnAsync call for group create to simulate AuthorizationFailed.
            _commandExecutor.ExecuteAsync("az",
                Arg.Is<string>(s => s.Contains("group create")),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                true,
                Arg.Any<CancellationToken>())
                .Returns(new CommandResult { ExitCode = 1, StandardError = "ERROR: (AuthorizationFailed) The client does not have authorization to perform action" });

            // Act - AzWarnAsync handles AuthorizationFailed via ExceptionHandler, should not throw from CreateInfrastructureAsync
            await InfrastructureSubcommand.CreateInfrastructureAsync(
                _commandExecutor, "test-sub", "test-tenant", "test-rg", "eastus", "test-plan", "B1",
                "test-webapp", generatedConfigPath, deploymentProjectPath, ProjectPlatform.DotNet, logger,
                needDeployment: true, skipInfra: false, externalHosting: false, CancellationToken.None);

            // Assert - AzWarnAsync does not throw; it handles the error via ExceptionHandler
            // The test verifies the code path is exercised without crashing
        }
        finally
        {
            if (File.Exists(generatedConfigPath)) File.Delete(generatedConfigPath);
            if (Directory.Exists(deploymentProjectPath)) Directory.Delete(deploymentProjectPath, true);
        }
    }

    #endregion

    private sealed class TestLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public bool HasWarning(string fragment) =>
            _entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(fragment));

        public bool HasInformation(string fragment) =>
            _entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains(fragment));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
