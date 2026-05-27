// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class BlueprintRegistrationRequirementCheckTests
{
    private readonly GraphApiService _mockGraphApiService;
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("test");

    private const string TestTenantId = "00000000-0000-0000-0000-000000000001";
    private const string TestBlueprintId = "00000000-0000-0000-0000-000000000002";
    private const string TestServicePrincipalId = "00000000-0000-0000-0000-000000000003";
    private const string TestRegistrationId = "reg-12345";

    public BlueprintRegistrationRequirementCheckTests()
    {
        _mockGraphApiService = Substitute.ForPartsOf<GraphApiService>();
    }

    // --- Metadata ---

    [Fact]
    public void Name_ReturnsBlueprintRegistration()
    {
        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        check.Name.Should().Be("Blueprint Registration");
    }

    [Fact]
    public void Category_ReturnsRegistration()
    {
        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        check.Category.Should().Be("Registration");
    }

    // --- Missing config ---

    [Fact]
    public async Task CheckAsync_NoBlueprintId_ReturnsFail()
    {
        var config = new Agent365Config { TenantId = TestTenantId };
        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);

        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse(because: "blueprintId is required");
        result.ErrorMessage.Should().Contain("blueprint ID not found",
            because: "error should indicate missing blueprint ID");
    }

    [Fact]
    public async Task CheckAsync_NoTenantId_ReturnsFail()
    {
        var config = new Agent365Config { AgentBlueprintId = TestBlueprintId };
        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);

        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse(because: "tenantId is required");
        result.ErrorMessage.Should().Contain("Tenant ID not found",
            because: "error should indicate missing tenant ID");
    }

    // --- Application check ---

    [Fact]
    public async Task CheckAsync_AppNotFound_ReturnsFail()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(false);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse(because: "application does not exist in Entra");
        result.ErrorMessage.Should().Contain("not found in Entra ID",
            because: "error should indicate app not found");
    }

    [Fact]
    public async Task CheckAsync_AppCheckThrows_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "auth/network errors are warnings, not failures");
        result.IsWarning.Should().BeTrue(because: "Graph API errors should produce a warning");
    }

    // --- Service principal check ---

    [Fact]
    public async Task CheckAsync_AppExistsButNoServicePrincipal_ReturnsFail()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns((string?)null);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse(because: "service principal must exist");
        result.ErrorMessage.Should().Contain("Service principal not found",
            because: "error should indicate missing service principal");
    }

    [Fact]
    public async Task CheckAsync_ServicePrincipalCheckThrows_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .ThrowsAsync(new HttpRequestException("Token expired"));

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "network errors on service principal check are warnings");
        result.IsWarning.Should().BeTrue();
    }

    // --- Full success without registration ---

    [Fact]
    public async Task CheckAsync_AppAndServicePrincipalExist_NoRegistrationId_ReturnsSuccess()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "app and service principal exist");
        result.IsWarning.Should().BeFalse();
        result.Details.Should().Contain(TestBlueprintId,
            because: "details should mention the blueprint ID");
    }

    // --- Agent registration checks ---

    [Fact]
    public async Task CheckAsync_RegistrationExists_ReturnsSuccess()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            AgentRegistrationId = TestRegistrationId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);
        _mockGraphApiService.AgentRegistrationExistsAsync(TestTenantId, TestRegistrationId, Arg.Any<CancellationToken>())
            .Returns(true);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "all registration checks passed");
        result.IsWarning.Should().BeFalse();
        result.Details.Should().Contain(TestRegistrationId,
            because: "details should mention the registration ID");
    }

    [Fact]
    public async Task CheckAsync_RegistrationNotFound_ReturnsFail()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            AgentRegistrationId = TestRegistrationId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);
        _mockGraphApiService.AgentRegistrationExistsAsync(TestTenantId, TestRegistrationId, Arg.Any<CancellationToken>())
            .Returns(false);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse(because: "agent registration does not exist");
        result.ErrorMessage.Should().Contain("not found",
            because: "error should indicate registration not found");
    }

    [Fact]
    public async Task CheckAsync_RegistrationUnknown_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            AgentRegistrationId = TestRegistrationId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);
        _mockGraphApiService.AgentRegistrationExistsAsync(TestTenantId, TestRegistrationId, Arg.Any<CancellationToken>())
            .Returns((bool?)null);

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "unknown registration status is a warning, not a failure");
        result.IsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_RegistrationCheckThrows_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            AgentRegistrationId = TestRegistrationId
        };

        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);
        _mockGraphApiService.AgentRegistrationExistsAsync(TestTenantId, TestRegistrationId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Forbidden"));

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "registration check errors are warnings");
        result.IsWarning.Should().BeTrue();
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_NullGraphApiService_Throws()
    {
        var act = () => new BlueprintRegistrationRequirementCheck(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Inheritable permissions checks ---

    private AgentBlueprintService CreateMockBlueprintService()
    {
        var bpLogger = NullLoggerFactory.Instance.CreateLogger<AgentBlueprintService>();
        return Substitute.ForPartsOf<AgentBlueprintService>(bpLogger, _mockGraphApiService);
    }

    private void SetupAppAndSpExist()
    {
        _mockGraphApiService.ApplicationExistsByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockGraphApiService.LookupServicePrincipalByAppIdAsync(TestTenantId, TestBlueprintId, Arg.Any<CancellationToken>(), Arg.Any<IEnumerable<string>?>())
            .Returns(TestServicePrincipalId);
    }

    [Fact]
    public async Task CheckAsync_WithPermissions_IncludesScopesInDetails()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            ResourceConsents = new List<ResourceConsent>
            {
                new()
                {
                    ResourceName = "Microsoft Graph",
                    ResourceAppId = "00000003-0000-0000-c000-000000000000",
                    ConsentGranted = true,
                    Scopes = new List<string> { "User.Read", "Mail.Read" }
                }
            }
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();
        mockBpService.ListInheritablePermissionsAsync(TestTenantId, TestBlueprintId, Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<(string ResourceAppId, List<string> Scopes)>
            {
                ("00000003-0000-0000-c000-000000000000", new List<string> { "User.Read", "Mail.Read" })
            });

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "all expected scopes are present in Entra");
        result.IsWarning.Should().BeFalse();
        result.Details.Should().Contain("User.Read",
            because: "details should list the configured scopes");
    }

    [Fact]
    public async Task CheckAsync_MissingScopes_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            ResourceConsents = new List<ResourceConsent>
            {
                new()
                {
                    ResourceName = "Microsoft Graph",
                    ResourceAppId = "00000003-0000-0000-c000-000000000000",
                    ConsentGranted = true,
                    Scopes = new List<string> { "User.Read", "Mail.Read", "Mail.Send" }
                }
            }
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();
        mockBpService.ListInheritablePermissionsAsync(TestTenantId, TestBlueprintId, Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<(string ResourceAppId, List<string> Scopes)>
            {
                ("00000003-0000-0000-c000-000000000000", new List<string> { "User.Read" })
            });

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "missing scopes is a warning, not a failure");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("Mail.Read",
            because: "warning should list the missing scopes");
        result.Details.Should().Contain("Mail.Send",
            because: "warning should list all missing scopes");
    }

    [Fact]
    public async Task CheckAsync_ResourceNotInEntra_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            ResourceConsents = new List<ResourceConsent>
            {
                new()
                {
                    ResourceName = "Agent 365 Tools",
                    ResourceAppId = "ea9ffc3e-8a23-4a7d-836d-234d7c7565c1",
                    ConsentGranted = true,
                    Scopes = new List<string> { "McpServers.DASearch.All" }
                }
            }
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();
        mockBpService.ListInheritablePermissionsAsync(TestTenantId, TestBlueprintId, Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<(string ResourceAppId, List<string> Scopes)>());

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "missing resource permissions is a warning, not a failure");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("Agent 365 Tools",
            because: "warning should name the resource missing permissions");
    }

    [Fact]
    public async Task CheckAsync_ConsentNotGranted_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            ResourceConsents = new List<ResourceConsent>
            {
                new()
                {
                    ResourceName = "Microsoft Graph",
                    ResourceAppId = "00000003-0000-0000-c000-000000000000",
                    ConsentGranted = false,
                    Scopes = new List<string> { "User.Read" }
                }
            }
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();
        mockBpService.ListInheritablePermissionsAsync(TestTenantId, TestBlueprintId, Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<(string ResourceAppId, List<string> Scopes)>
            {
                ("00000003-0000-0000-c000-000000000000", new List<string> { "User.Read" })
            });

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "consent issues are warnings, not failures");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("admin consent not granted",
            because: "warning should indicate consent is missing");
    }

    [Fact]
    public async Task CheckAsync_NoResourceConsentsInConfig_SkipsPermissionsCheck()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "no resource consents in config means permissions check is skipped");
        result.IsWarning.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_PermissionsCheckThrows_ReturnsWarning()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId,
            ResourceConsents = new List<ResourceConsent>
            {
                new()
                {
                    ResourceAppId = "00000003-0000-0000-c000-000000000000",
                    ConsentGranted = true,
                    Scopes = new List<string> { "User.Read" }
                }
            }
        };

        SetupAppAndSpExist();
        var mockBpService = CreateMockBlueprintService();
        mockBpService.ListInheritablePermissionsAsync(TestTenantId, TestBlueprintId, Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Forbidden"));

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, mockBpService);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "permissions query errors are warnings");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("Permissions query failed",
            because: "warning should indicate what went wrong");
    }

    [Fact]
    public async Task CheckAsync_NoBlueprintService_SkipsPermissionsCheck()
    {
        var config = new Agent365Config
        {
            TenantId = TestTenantId,
            AgentBlueprintId = TestBlueprintId
        };

        SetupAppAndSpExist();

        var check = new BlueprintRegistrationRequirementCheck(_mockGraphApiService, blueprintService: null);
        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "without blueprint service, permissions check is skipped");
        result.IsWarning.Should().BeFalse(because: "skipping permissions check is not a warning");
    }
}
