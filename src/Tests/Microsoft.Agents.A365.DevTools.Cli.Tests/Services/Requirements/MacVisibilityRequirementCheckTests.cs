// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class MacVisibilityRequirementCheckTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _tempDir;

    public MacVisibilityRequirementCheckTests()
    {
        _logger = Substitute.For<ILogger>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"a365-mac-validate-{Guid.NewGuid():N}");
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
    public void ParseNumericMetrics_FromMarkdown_ParsesKpiAndDailyValues()
    {
        var text = BuildMarkdownMetrics(
            activeUsersRl7: 9,
            invocationsRl7: 47,
            sessionsRl7: 17,
            toolExecutionsRl7: 60,
            inferenceCallsRl7: 0,
            runtimeHrsRl7: 0.15,
            exceptionRateRl7: 0,
            activeUsersRl30: 18,
            invocationsRl30: 362,
            sessionsRl30: 118,
            toolExecutionsRl30: 1452,
            inferenceCallsRl30: 431,
            runtimeHrsRl30: 13.8,
            exceptionRateRl30: 0);

        var parsed = MacVisibilityRequirementCheck.ParseNumericMetrics(text);

        parsed["kpi.active_users.rl7"].Should().Be(9);
        parsed["kpi.invocations.rl30"].Should().Be(362);
        parsed["kpi.runtime_hrs.rl7"].Should().Be(0.15);
        parsed["kpi.exception_rate.rl30"].Should().Be(0);
        parsed["daily.may_26.users"].Should().Be(4);
        parsed["daily.may_26.invocations"].Should().Be(12);
        parsed["daily.may_26.sessions"].Should().Be(4);
    }

    [Fact]
    public void CompareMetrics_NonExceptionMetricNotIncreased_Fails()
    {
        var baseline = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["kpi.invocations.rl7"] = 10,
            ["kpi.exception_rate.rl7"] = 1
        };
        var current = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["kpi.invocations.rl7"] = 10,
            ["kpi.exception_rate.rl7"] = 0
        };

        var comparisons = MacVisibilityRequirementCheck.CompareMetrics(baseline, current);

        comparisons.Should().ContainSingle(c => c.MetricKey == "kpi.invocations.rl7" && !c.Passed,
            because: "required non-exception metrics must strictly increase");
        comparisons.Should().ContainSingle(c => c.MetricKey == "kpi.exception_rate.rl7" && c.Passed,
            because: "exception rate is exempt from increase requirement");
    }

    [Fact]
    public async Task CaptureInitialMetricsAsync_WritesBaselineAndCheckAsync_PassesOnIncrease()
    {
        var baselinePath = Path.Combine(_tempDir, MacVisibilityRequirementCheck.BaselineFileName);
        var config = new Agent365Config
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AgentIdentityDisplayName = "TestAgent",
            Agent365ObservabilityMcpOptions = new Agent365ObservabilityMcpOptions
            {
                AgentObservabilityId = "11111111-2222-3333-4444-555555555555"
            }
        };

        var baselineText = BuildMarkdownMetrics(
            activeUsersRl7: 9,
            invocationsRl7: 47,
            sessionsRl7: 17,
            toolExecutionsRl7: 60,
            inferenceCallsRl7: 0,
            runtimeHrsRl7: 0.15,
            exceptionRateRl7: 0,
            activeUsersRl30: 18,
            invocationsRl30: 362,
            sessionsRl30: 118,
            toolExecutionsRl30: 1452,
            inferenceCallsRl30: 431,
            runtimeHrsRl30: 13.8,
            exceptionRateRl30: 0);

        var postText = BuildMarkdownMetrics(
            activeUsersRl7: 10,
            invocationsRl7: 48,
            sessionsRl7: 18,
            toolExecutionsRl7: 61,
            inferenceCallsRl7: 1,
            runtimeHrsRl7: 0.20,
            exceptionRateRl7: 0,
            activeUsersRl30: 19,
            invocationsRl30: 363,
            sessionsRl30: 119,
            toolExecutionsRl30: 1453,
            inferenceCallsRl30: 432,
            runtimeHrsRl30: 13.9,
            exceptionRateRl30: 0);

        var captureHandler = new StaticToolResponseHandler(CreateJsonRpcResponseWithText(baselineText));
        var capture = await MacVisibilityRequirementCheck.CaptureInitialMetricsAsync(
            config,
            _logger,
            authService: null,
            environment: "prod",
            baseUrlOverride: "https://example.test",
            baselineFilePath: baselinePath,
            httpHandler: captureHandler,
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        capture.Passed.Should().BeTrue();
        File.Exists(baselinePath).Should().BeTrue();

        var checkHandler = new StaticToolResponseHandler(CreateJsonRpcResponseWithText(postText));
        var check = new MacVisibilityRequirementCheck(
            authService: null,
            baselineFilePath: baselinePath,
            conversationStepVerified: true,
            environment: "prod",
            baseUrlOverride: "https://example.test",
            httpHandler: checkHandler,
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeTrue(because: "all required KPI metrics increased and exception rate is exempt");
        result.Metadata!.MacMetricComparisons.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenConversationNotVerified_Fails()
    {
        var baselinePath = Path.Combine(_tempDir, MacVisibilityRequirementCheck.BaselineFileName);
        var config = new Agent365Config
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AgentIdentityDisplayName = "TestAgent"
        };

        var text = BuildMarkdownMetrics(
            activeUsersRl7: 1,
            invocationsRl7: 1,
            sessionsRl7: 1,
            toolExecutionsRl7: 1,
            inferenceCallsRl7: 1,
            runtimeHrsRl7: 1,
            exceptionRateRl7: 0,
            activeUsersRl30: 1,
            invocationsRl30: 1,
            sessionsRl30: 1,
            toolExecutionsRl30: 1,
            inferenceCallsRl30: 1,
            runtimeHrsRl30: 1,
            exceptionRateRl30: 0);

        await File.WriteAllTextAsync(
            baselinePath,
            "{\"capturedAtUtc\":\"2026-01-01T00:00:00Z\",\"endpoint\":\"https://example\",\"toolName\":\"getAgentMetrics\",\"serverName\":\"observability-mcp\",\"numericMetrics\":{\"kpi.invocations.rl7\":1}}",
            Encoding.UTF8);

        var check = new MacVisibilityRequirementCheck(
            authService: null,
            baselineFilePath: baselinePath,
            conversationStepVerified: false,
            baseUrlOverride: "https://example.test",
            httpHandler: new StaticToolResponseHandler(CreateJsonRpcResponseWithText(text)),
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        var result = await check.CheckAsync(config, _logger);

        result.Passed.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Conversation simulation step is not verified");
    }

    [Fact]
    public async Task CaptureInitialMetricsAsync_BlankOverrides_FallsBackToConfigValues()
    {
        var baselinePath = Path.Combine(_tempDir, MacVisibilityRequirementCheck.BaselineFileName);
        var config = new Agent365Config
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AgentIdentityDisplayName = "Fallback Agent",
            Agent365ObservabilityMcpOptions = new Agent365ObservabilityMcpOptions
            {
                BaseUrl = "https://example.test/",
                TenantId = "22222222-2222-2222-2222-222222222222",
                AgentName = "Configured Agent"
            }
        };

        var handler = new StaticToolResponseHandler(CreateJsonRpcResponseWithText(BuildMarkdownMetrics(
            activeUsersRl7: 1,
            invocationsRl7: 2,
            sessionsRl7: 3,
            toolExecutionsRl7: 4,
            inferenceCallsRl7: 5,
            runtimeHrsRl7: 0.5,
            exceptionRateRl7: 0,
            activeUsersRl30: 10,
            invocationsRl30: 20,
            sessionsRl30: 30,
            toolExecutionsRl30: 40,
            inferenceCallsRl30: 50,
            runtimeHrsRl30: 5,
            exceptionRateRl30: 0)));

        var result = await MacVisibilityRequirementCheck.CaptureInitialMetricsAsync(
            config,
            _logger,
            authService: null,
            environment: "prod",
            baseUrlOverride: string.Empty,
            tenantIdOverride: string.Empty,
            agentNameOverride: string.Empty,
            baselineFilePath: baselinePath,
            httpHandler: handler,
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        result.Passed.Should().BeTrue(because: "blank overrides should not block fallback to configured observability MCP values");
    }

    [Fact]
    public async Task CaptureInitialMetricsAsync_ProbesToolsListBeforeCallingGetAgentMetrics()
    {
        var baselinePath = Path.Combine(_tempDir, MacVisibilityRequirementCheck.BaselineFileName);
        var config = new Agent365Config
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AgentIdentityDisplayName = "TestAgent"
        };

        var handler = new SequencedMcpResponseHandler(
            CreateToolsListResponse(MacVisibilityRequirementCheck.GetAgentMetricsToolName, "otherTool"),
            CreateJsonRpcResponseWithText(BuildMarkdownMetrics(
                activeUsersRl7: 1,
                invocationsRl7: 2,
                sessionsRl7: 3,
                toolExecutionsRl7: 4,
                inferenceCallsRl7: 5,
                runtimeHrsRl7: 0.5,
                exceptionRateRl7: 0,
                activeUsersRl30: 10,
                invocationsRl30: 20,
                sessionsRl30: 30,
                toolExecutionsRl30: 40,
                inferenceCallsRl30: 50,
                runtimeHrsRl30: 5,
                exceptionRateRl30: 0)));

        var result = await MacVisibilityRequirementCheck.CaptureInitialMetricsAsync(
            config,
            _logger,
            authService: null,
            environment: "prod",
            baseUrlOverride: "https://example.test",
            baselineFilePath: baselinePath,
            httpHandler: handler,
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        result.Passed.Should().BeTrue();
        handler.Methods.Should().ContainInOrder("tools/list", "tools/call");
        handler.ToolCallArgumentNames.Should().Contain("agentName",
            because: "when agentObservabilityId is not configured, the MAC check falls back to agentName");
        HasLogMessageContaining("advertised getAgentMetrics: True").Should().BeTrue(
            because: "successful discovery should record whether the server advertises getAgentMetrics");
    }

    [Fact]
    public async Task CaptureInitialMetricsAsync_ToolsListFailure_FailsWithoutCallingGetAgentMetrics()
    {
        var baselinePath = Path.Combine(_tempDir, MacVisibilityRequirementCheck.BaselineFileName);
        var config = new Agent365Config
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AgentIdentityDisplayName = "TestAgent"
        };

        var handler = new SequencedMcpResponseHandler(
            "{\"error\":\"boom\"}",
            HttpStatusCode.InternalServerError,
            CreateJsonRpcResponseWithText(BuildMarkdownMetrics(
                activeUsersRl7: 1,
                invocationsRl7: 2,
                sessionsRl7: 3,
                toolExecutionsRl7: 4,
                inferenceCallsRl7: 5,
                runtimeHrsRl7: 0.5,
                exceptionRateRl7: 0,
                activeUsersRl30: 10,
                invocationsRl30: 20,
                sessionsRl30: 30,
                toolExecutionsRl30: 40,
                inferenceCallsRl30: 50,
                runtimeHrsRl30: 5,
                exceptionRateRl30: 0)));

        var result = await MacVisibilityRequirementCheck.CaptureInitialMetricsAsync(
            config,
            _logger,
            authService: null,
            environment: "prod",
            baseUrlOverride: "https://example.test",
            baselineFilePath: baselinePath,
            httpHandler: handler,
            tokenProviderOverride: _ => Task.FromResult<string?>("token"));

        result.Passed.Should().BeFalse(because: "tools/list must advertise getAgentMetrics before tools/call is attempted");
        result.Details.Should().Contain("required tool 'getAgentMetrics'", because: "discovery should fail fast when required tool is not listed");
        handler.Methods.Should().Equal(new[] { "tools/list" },
            because: "tools/call should not run when required tool discovery fails");
        HasLogMessageContaining("tools/list probe failed").Should().BeTrue(
            because: "discovery failures should be logged distinctly from getAgentMetrics invocation failures");
    }

    private static string CreateJsonRpcResponseWithText(string text)
    {
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return
            "{" +
            "\"jsonrpc\":\"2.0\"," +
            "\"id\":\"1\"," +
            "\"result\":{" +
            "\"content\":[{" +
            "\"text\":\"" + escaped + "\"" +
            "}]" +
            "}" +
            "}";
    }

    private static string CreateToolsListResponse(params string[] toolNames)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = "tools-list",
            result = new
            {
                tools = toolNames.Select(name => new { name }).ToArray()
            }
        });
    }

    private static string BuildMarkdownMetrics(
        double activeUsersRl7,
        double invocationsRl7,
        double sessionsRl7,
        double toolExecutionsRl7,
        double inferenceCallsRl7,
        double runtimeHrsRl7,
        double exceptionRateRl7,
        double activeUsersRl30,
        double invocationsRl30,
        double sessionsRl30,
        double toolExecutionsRl30,
        double inferenceCallsRl30,
        double runtimeHrsRl30,
        double exceptionRateRl30)
    {
        return
            "2. getAgentMetrics - KPIs + daily time series\n\n" +
            "| Metric | RL7 | RL30 | WoW Change |\n" +
            "|---|---:|---:|---:|\n" +
            $"| Active Users | {activeUsersRl7} | {activeUsersRl30} | -10% |\n" +
            $"| Invocations | {invocationsRl7} | {invocationsRl30} | -63% |\n" +
            $"| Sessions | {sessionsRl7} | {sessionsRl30} | - |\n" +
            $"| Tool Executions | {toolExecutionsRl7} | {toolExecutionsRl30} | - |\n" +
            $"| Inference Calls | {inferenceCallsRl7} | {inferenceCallsRl30} | - |\n" +
            $"| Runtime (hrs) | {runtimeHrsRl7} | {runtimeHrsRl30} | -93.9% |\n" +
            $"| Exception Rate | {exceptionRateRl7}% | {exceptionRateRl30}% | 0% |\n\n" +
            "Daily time series (last 5 days):\n" +
            "| Date | Users | Invocations | Sessions |\n" +
            "|---|---:|---:|---:|\n" +
            "| May 26 | 4 | 12 | 4 |\n";
    }

    private bool HasLogMessageContaining(string expectedText)
    {
        return _logger.ReceivedCalls().Any(call =>
        {
            if (!string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal))
            {
                return false;
            }

            var state = call.GetArguments()[2];
            return state?.ToString()?.Contains(expectedText, StringComparison.OrdinalIgnoreCase) == true;
        });
    }

    private sealed class StaticToolResponseHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public StaticToolResponseHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responseContent = _responseContent;
            if (request.Content is not null)
            {
                var requestJson = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(requestJson);
                var method = document.RootElement.GetProperty("method").GetString();
                if (string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
                {
                    responseContent = CreateToolsListResponse(MacVisibilityRequirementCheck.GetAgentMetricsToolName, "otherTool");
                }
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class SequencedMcpResponseHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses;

        public SequencedMcpResponseHandler(string responseContent, HttpStatusCode statusCode, params string[] additionalResponseContents)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Content)>();
            _responses.Enqueue((statusCode, responseContent));

            foreach (var content in additionalResponseContents)
            {
                _responses.Enqueue((HttpStatusCode.OK, content));
            }
        }

        public SequencedMcpResponseHandler(params string[] responseContents)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Content)>(
                responseContents.Select(content => (HttpStatusCode.OK, content)));
        }

        public List<string> Methods { get; } = [];
        public List<string> ToolCallArgumentNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Content.Should().NotBeNull();

            var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(requestJson);
            var method = document.RootElement.GetProperty("method").GetString() ?? string.Empty;
            Methods.Add(method);

            if (string.Equals(method, "tools/call", StringComparison.OrdinalIgnoreCase)
                && document.RootElement.TryGetProperty("params", out var paramsElement)
                && paramsElement.TryGetProperty("arguments", out var argumentsElement)
                && argumentsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentsElement.EnumerateObject())
                {
                    ToolCallArgumentNames.Add(property.Name);
                }
            }

            var (statusCode, content) = _responses.Dequeue();
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
