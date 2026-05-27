// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class AgentMetricsRequirementCheckTests
{
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("test");

    [Fact]
    public void Name_ReturnsAgentMetrics()
    {
        var check = new AgentMetricsRequirementCheck();
        check.Name.Should().Be("AgentMetrics");
    }

    [Fact]
    public void Category_ReturnsObservability()
    {
        var check = new AgentMetricsRequirementCheck();
        check.Category.Should().Be("Observability");
    }

    [Fact]
    public async Task CheckAsync_BaselineMetricsNull_ConversationSucceeds_ReturnsFail()
    {
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: null,
            conversationResult: true);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeFalse(because: "metrics endpoint must be reachable even if conversation succeeded");
        result.ErrorMessage.Should().Contain("not available",
            because: "error should indicate metrics endpoint was unreachable");
    }

    [Fact]
    public async Task CheckAsync_BaselineMetricsNull_ConversationFails_ReturnsWarning()
    {
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: null,
            conversationResult: false);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeTrue(because: "conversation failure is a warning");
        result.IsWarning.Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be generated",
            because: "warning should indicate conversation generation failed");
    }

    [Fact]
    public async Task CheckAsync_BaselineMetricsThrows_ConversationSucceeds_ReturnsFail()
    {
        var check = new TestableAgentMetricsCheck(
            metricsException: new HttpRequestException("Connection refused"),
            conversationResult: true);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeFalse(because: "metrics endpoint must be reachable");
        result.ErrorMessage.Should().Contain("not available",
            because: "error should indicate metrics endpoint was unreachable");
    }

    [Fact]
    public async Task CheckAsync_ConversationFails_ReturnsWarning()
    {
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: new AgentMetricsSnapshot { InvocationCount = 5 },
            conversationResult: false);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeTrue(because: "conversation failure is a warning");
        result.IsWarning.Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be generated",
            because: "warning should indicate conversation generation failed");
    }

    [Fact]
    public async Task CheckAsync_ConversationThrows_ReturnsWarning()
    {
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: new AgentMetricsSnapshot { InvocationCount = 5 },
            conversationException: new InvalidOperationException("Browser not found"));
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeTrue(because: "Playwright errors are warnings");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("Browser not found",
            because: "warning details should contain the Playwright error");
    }

    [Fact]
    public async Task CheckAsync_MetricsNotIncremented_ReturnsFail()
    {
        var snapshot = new AgentMetricsSnapshot { InvocationCount = 5 };
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: snapshot,
            postConversationMetrics: snapshot,
            conversationResult: true);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeFalse(because: "metrics should have incremented after conversation");
        result.ErrorMessage.Should().Contain("did not increment",
            because: "error should describe the metrics did not change");
        result.Details.Should().Contain("Baseline invocations: 5",
            because: "details should show the baseline count");
    }

    [Fact]
    public async Task CheckAsync_MetricsIncremented_ReturnsSuccess()
    {
        var baseline = new AgentMetricsSnapshot { InvocationCount = 5 };
        var postConversation = new AgentMetricsSnapshot { InvocationCount = 8 };
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: baseline,
            postConversationMetrics: postConversation,
            conversationResult: true);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeTrue(because: "metrics incremented from 5 to 8");
        result.IsWarning.Should().BeFalse();
        result.Details.Should().Contain("incremented from 5 to 8",
            because: "details should show the increment");
    }

    [Fact]
    public async Task CheckAsync_PostConversationMetricsNull_ReturnsFail()
    {
        var baseline = new AgentMetricsSnapshot { InvocationCount = 5 };
        var check = new TestableAgentMetricsCheck(
            baselineMetrics: baseline,
            postConversationMetrics: null,
            conversationResult: true,
            postConversationMetricsExplicitNull: true);
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeFalse(because: "post-conversation metrics must be retrievable");
        result.ErrorMessage.Should().Contain("not available after conversation",
            because: "error should indicate post-conversation metrics failed");
    }

    [Fact]
    public async Task CheckAsync_DefaultPlaceholder_ReturnsWarning()
    {
        // Default check has no playwright service, so conversation returns false
        var check = new AgentMetricsRequirementCheck();
        var result = await check.CheckAsync(new Agent365Config(), _logger);

        result.Passed.Should().BeTrue(because: "conversation failure is a warning");
        result.IsWarning.Should().BeTrue();
    }

    /// <summary>
    /// Testable subclass that overrides the virtual methods for controlled test behavior.
    /// </summary>
    private sealed class TestableAgentMetricsCheck : AgentMetricsRequirementCheck
    {
        private readonly AgentMetricsSnapshot? _baselineMetrics;
        private readonly AgentMetricsSnapshot? _postConversationMetrics;
        private readonly bool _postConversationMetricsExplicitNull;
        private readonly bool _conversationResult;
        private readonly Exception? _metricsException;
        private readonly Exception? _conversationException;
        private int _metricsCallCount;

        public TestableAgentMetricsCheck(
            AgentMetricsSnapshot? baselineMetrics = null,
            AgentMetricsSnapshot? postConversationMetrics = null,
            bool conversationResult = false,
            Exception? metricsException = null,
            Exception? conversationException = null,
            bool postConversationMetricsExplicitNull = false)
        {
            _baselineMetrics = baselineMetrics;
            _postConversationMetrics = postConversationMetricsExplicitNull ? null : (postConversationMetrics ?? baselineMetrics);
            _postConversationMetricsExplicitNull = postConversationMetricsExplicitNull;
            _conversationResult = conversationResult;
            _metricsException = metricsException;
            _conversationException = conversationException;
        }

        protected internal override Task<AgentMetricsSnapshot?> GetAgentMetricsAsync(
            Agent365Config config, ILogger logger, CancellationToken cancellationToken)
        {
            if (_metricsException is not null)
                throw _metricsException;

            _metricsCallCount++;
            return Task.FromResult(_metricsCallCount == 1 ? _baselineMetrics : _postConversationMetrics);
        }

        protected internal override Task<bool> GenerateCopilotChatConversationAsync(
            Agent365Config config, ILogger logger, CancellationToken cancellationToken)
        {
            if (_conversationException is not null)
                throw _conversationException;

            return Task.FromResult(_conversationResult);
        }
    }
}
