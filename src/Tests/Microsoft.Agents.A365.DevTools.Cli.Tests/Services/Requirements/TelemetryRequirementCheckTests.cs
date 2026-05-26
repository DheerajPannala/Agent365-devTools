// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services.Requirements;

public class TelemetryRequirementCheckTests : IDisposable
{
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("test");
    private readonly Agent365Config _config = new();
    private readonly List<string> _tempFiles = new();

    private string CreateTempLogFile(string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"telemetry-test-{Guid.NewGuid()}.log");
        File.WriteAllLines(path, lines);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Helper to build a console exporter span block with Agent365Sdk scope.
    /// </summary>
    private static string[] MakeAgent365Span(string operationName) => new[]
    {
        "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
        "  instrumentationScope: {",
        "    name: 'Agent365Sdk',",
        "  },",
        $"    'gen_ai.operation.name': '{operationName}',"
    };

    /// <summary>
    /// Helper to build a fully-compliant span block with scope version and parentId.
    /// </summary>
    private static string[] MakeFullAgent365Span(string operationName, bool withParent = false) => withParent
        ? new[]
        {
            "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
            "  parentId: 'abc123def456',",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "    version: '1.0.0',",
            "  },",
            $"    'gen_ai.operation.name': '{operationName}',"
        }
        : new[]
        {
            "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "    version: '1.0.0',",
            "  },",
            $"    'gen_ai.operation.name': '{operationName}',"
        };

    /// <summary>
    /// Resource lines that satisfy OTel semantic convention checks.
    /// </summary>
    private static readonly string[] ResourceLines = new[]
    {
        "  resource: {",
        "    'telemetry.sdk.name': 'opentelemetry',",
        "    'telemetry.sdk.version': '1.25.0',",
        "    'service.name': 'my-agent',",
        "  },"
    };

    /// <summary>
    /// Helper to build a span block with a non-Agent365 scope (but not the ignored scope).
    /// These spans SHOULD be accepted by the check.
    /// </summary>
    private static string[] MakeOtherSpan(string operationName) => new[]
    {
        "  traceId: 'aaaa028f0ee6a6cbb3b0e3c96ee96fa7',",
        "  instrumentationScope: {",
        "    name: 'microsoft-otel-langchain',",
        "  },",
        $"    'gen_ai.operation.name': '{operationName}',"
    };

    /// <summary>
    /// Helper to build a span block from the ignored @microsoft/agents-telemetry scope.
    /// These spans should be EXCLUDED from validation.
    /// </summary>
    private static string[] MakeIgnoredScopeSpan(string operationName) => new[]
    {
        "  traceId: 'bbbb028f0ee6a6cbb3b0e3c96ee96fa7',",
        "  instrumentationScope: {",
        "    name: '@microsoft/agents-telemetry',",
        "  },",
        $"    'gen_ai.operation.name': '{operationName}',"
    };

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { /* best-effort cleanup */ }
        }
    }

    // --- Metadata ---

    [Fact]
    public void Name_ReturnsTelemetry()
    {
        var check = new TelemetryRequirementCheck(null);
        check.Name.Should().Be("Telemetry");
    }

    [Fact]
    public void Category_ReturnsObservability()
    {
        var check = new TelemetryRequirementCheck(null);
        check.Category.Should().Be("Observability");
    }

    // --- No log file ---

    [Fact]
    public async Task CheckAsync_NullLogPath_ReturnsWarning()
    {
        var check = new TelemetryRequirementCheck(null);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "no log file is a warning, not a failure");
        result.IsWarning.Should().BeTrue(because: "missing log file means telemetry status is unknown");
    }

    [Fact]
    public async Task CheckAsync_NonExistentLogPath_ReturnsWarning()
    {
        var check = new TelemetryRequirementCheck("/nonexistent/path.log");

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "missing file is a warning, not a failure");
        result.IsWarning.Should().BeTrue();
    }

    // --- No span output ---

    [Fact]
    public async Task CheckAsync_NoSpanOutput_ReturnsFail()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "info: Listening on http://localhost:5000"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "no console exporter span output detected");
        result.ErrorMessage.Should().Contain("No console exporter span output detected");
    }

    // --- Ignored scope exclusion ---

    [Fact]
    public async Task CheckAsync_SpansOnlyFromIgnoredScope_ReturnsFail()
    {
        var lines = new List<string>();
        lines.AddRange(MakeIgnoredScopeSpan("invoke_agent"));
        lines.AddRange(MakeIgnoredScopeSpan("chat"));
        lines.AddRange(MakeIgnoredScopeSpan("execute_tool"));
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "spans from @microsoft/agents-telemetry scope should be excluded");
        result.Details.Should().Contain("@microsoft/agents-telemetry",
            because: "details should indicate the ignored scope that caused all spans to be filtered out");
    }

    // --- All 3 GenAI spans from Agent365Sdk ---

    [Fact]
    public async Task CheckAsync_AllThreeSpansFromAgent365Sdk_ReturnsPass()
    {
        var lines = new List<string>();
        lines.AddRange(MakeAgent365Span("invoke_agent"));
        lines.AddRange(MakeAgent365Span("chat"));
        lines.AddRange(MakeAgent365Span("execute_tool"));
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "all 3 required GenAI spans are present from Agent365Sdk scope");
        result.Details.Should().Contain("All required GenAI operation spans detected");
    }

    [Fact]
    public async Task CheckAsync_MixedScopes_ExcludesIgnoredScope()
    {
        var lines = new List<string>();
        // Other scope has invoke_agent and chat — should count
        lines.AddRange(MakeOtherSpan("invoke_agent"));
        lines.AddRange(MakeOtherSpan("chat"));
        // Ignored scope has execute_tool — should NOT count
        lines.AddRange(MakeIgnoredScopeSpan("execute_tool"));
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "execute_tool from @microsoft/agents-telemetry scope should not count");
        result.ErrorMessage.Should().Contain("execute_tool");
    }

    [Fact]
    public async Task CheckAsync_OtherScopes_AllAccepted()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "  traceId: 'abc',",
            "  instrumentationScope: {",
            "    name: 'CustomSdk',",
            "  },",
            "    'gen_ai.operation.name': 'invoke_agent',",
            "  traceId: 'def',",
            "  instrumentationScope: {",
            "    name: 'microsoft-otel-langchain',",
            "  },",
            "    'gen_ai.operation.name': 'chat',",
            "  traceId: 'ghi',",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "  },",
            "    'gen_ai.operation.name': 'execute_tool',"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "all scopes except @microsoft/agents-telemetry should be accepted");
    }

    [Fact]
    public async Task CheckAsync_IgnoredScope_CaseInsensitiveExclusion()
    {
        var lines = new List<string>();
        // Ignored scope with different casing — should still be excluded
        lines.Add("  traceId: 'abc',");
        lines.Add("  instrumentationScope: {");
        lines.Add("    name: '@Microsoft/Agents-Telemetry',");
        lines.Add("  },");
        lines.Add("    'gen_ai.operation.name': 'invoke_agent',");
        lines.Add("  traceId: 'def',");
        lines.Add("  instrumentationScope: {");
        lines.Add("    name: '@MICROSOFT/AGENTS-TELEMETRY',");
        lines.Add("  },");
        lines.Add("    'gen_ai.operation.name': 'chat',");
        lines.Add("  traceId: 'ghi',");
        lines.Add("  instrumentationScope: {");
        lines.Add("    name: '@microsoft/agents-telemetry',");
        lines.Add("  },");
        lines.Add("    'gen_ai.operation.name': 'execute_tool',");
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "ignored scope exclusion should be case-insensitive");
    }

    // --- Missing spans ---

    [Fact]
    public async Task CheckAsync_MissingChat_ReturnsFail()
    {
        var lines = new List<string>();
        lines.AddRange(MakeAgent365Span("invoke_agent"));
        lines.AddRange(MakeAgent365Span("execute_tool"));
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "chat operation is missing");
        result.ErrorMessage.Should().Contain("chat");
    }

    [Fact]
    public async Task CheckAsync_OnlyInvokeAgent_ReportsOtherTwoMissing()
    {
        var lines = new List<string>();
        lines.AddRange(MakeAgent365Span("invoke_agent"));
        var logPath = CreateTempLogFile(lines.ToArray());

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse();
        result.ErrorMessage.Should().Contain("chat");
        result.ErrorMessage.Should().Contain("execute_tool");
    }

    // --- SplitIntoSpanBlocks ---

    [Fact]
    public void SplitIntoSpanBlocks_SplitsOnTraceId()
    {
        var lines = new[]
        {
            "  traceId: 'aaa',",
            "  name: 'span1',",
            "  traceId: 'bbb',",
            "  name: 'span2',"
        };

        var blocks = TelemetryRequirementCheck.SplitIntoSpanBlocks(lines);

        blocks.Should().HaveCount(2);
        blocks[0].Should().Contain(l => l.Contains("span1"));
        blocks[1].Should().Contain(l => l.Contains("span2"));
    }

    [Fact]
    public void SplitIntoSpanBlocks_IgnoresLinesBeforeFirstTraceId()
    {
        var lines = new[]
        {
            "info: Application started",
            "info: some noise",
            "  traceId: 'aaa',",
            "  name: 'span1',"
        };

        var blocks = TelemetryRequirementCheck.SplitIntoSpanBlocks(lines);

        blocks.Should().HaveCount(1);
    }

    [Fact]
    public void SplitIntoSpanBlocks_EmptyInput_ReturnsEmpty()
    {
        var blocks = TelemetryRequirementCheck.SplitIntoSpanBlocks(Array.Empty<string>());
        blocks.Should().BeEmpty();
    }

    [Fact]
    public void SplitIntoSpanBlocks_IncludesLinesBeforeTraceId()
    {
        // instrumentationScope appears before traceId in real output
        var lines = new[]
        {
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "  },",
            "  traceId: 'aaa',",
            "  'gen_ai.operation.name': 'chat',"
        };

        var blocks = TelemetryRequirementCheck.SplitIntoSpanBlocks(lines);

        // The lines before the first traceId won't be in any block
        // instrumentationScope needs to be AFTER traceId or we need to handle this
        blocks.Should().HaveCount(1);
    }

    // --- ExtractOperationNames ---

    [Fact]
    public void ExtractOperationNames_SingleQuoteFormat()
    {
        var block = new List<string> { "    'gen_ai.operation.name': 'chat'," };

        var result = TelemetryRequirementCheck.ExtractOperationNames(block);

        result.Should().ContainSingle().Which.Should().Be("chat");
    }

    [Fact]
    public void ExtractOperationNames_DoubleQuoteFormat()
    {
        var block = new List<string> { "    \"gen_ai.operation.name\": \"invoke_agent\"," };

        var result = TelemetryRequirementCheck.ExtractOperationNames(block);

        result.Should().ContainSingle().Which.Should().Be("invoke_agent");
    }

    [Fact]
    public void ExtractOperationNames_EqualsFormat()
    {
        var block = new List<string> { "gen_ai.operation.name=execute_tool" };

        var result = TelemetryRequirementCheck.ExtractOperationNames(block);

        result.Should().ContainSingle().Which.Should().Be("execute_tool");
    }

    [Fact]
    public void ExtractOperationNames_NoMatch_ReturnsEmpty()
    {
        var block = new List<string> { "  name: 'some-span',", "  duration: 123" };

        var result = TelemetryRequirementCheck.ExtractOperationNames(block);

        result.Should().BeEmpty();
    }

    // --- Real-world console exporter output ---

    [Fact]
    public async Task CheckAsync_RealWorldNodeConsoleExporter_ReturnsPass()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "{",
            "  resource: {",
            "    attributes: {",
            "      'service.name': 'internal-docs-agent',",
            "      'telemetry.sdk.name': 'opentelemetry',",
            "    }",
            "  },",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "    version: '1.0.0',",
            "  },",
            "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
            "  name: 'invoke_agent Agent',",
            "  attributes: {",
            "    'gen_ai.operation.name': 'invoke_agent',",
            "  },",
            "}",
            "{",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "  },",
            "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
            "  name: 'chat gpt-4.1',",
            "  attributes: {",
            "    'gen_ai.operation.name': 'chat',",
            "    'gen_ai.request.model': 'gpt-4.1-2025-04-14',",
            "  },",
            "}",
            "{",
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "  },",
            "  traceId: '59ea028f0ee6a6cbb3b0e3c96ee96fa7',",
            "  name: 'execute_tool search_docs',",
            "  attributes: {",
            "    'gen_ai.operation.name': 'execute_tool',",
            "  },",
            "}"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "real-world Node.js console exporter output with Agent365Sdk scope should pass");
        result.Details.Should().Contain("invoke_agent");
        result.Details.Should().Contain("chat");
        result.Details.Should().Contain("execute_tool");
    }

    // --- Scope version checks ---

    [Fact]
    public void HasInstrumentationScopeVersion_WithVersion_ReturnsTrue()
    {
        var block = new List<string>
        {
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "    version: '1.0.0',",
            "  },"
        };

        TelemetryRequirementCheck.HasInstrumentationScopeVersion(block).Should().BeTrue();
    }

    [Fact]
    public void HasInstrumentationScopeVersion_SameLine_ReturnsTrue()
    {
        var block = new List<string>
        {
            "  instrumentationScope: { name: 'Agent365Sdk', version: '1.0.0' },"
        };

        TelemetryRequirementCheck.HasInstrumentationScopeVersion(block).Should().BeTrue();
    }

    [Fact]
    public void HasInstrumentationScopeVersion_NoVersion_ReturnsFalse()
    {
        var block = new List<string>
        {
            "  instrumentationScope: {",
            "    name: 'Agent365Sdk',",
            "  },"
        };

        TelemetryRequirementCheck.HasInstrumentationScopeVersion(block).Should().BeFalse();
    }

    // --- Parent link checks ---

    [Fact]
    public void GetChildSpansMissingParent_WithParentId_ReturnsEmpty()
    {
        var blocks = new List<List<string>>
        {
            new(MakeFullAgent365Span("chat", withParent: true)),
            new(MakeFullAgent365Span("execute_tool", withParent: true))
        };

        TelemetryRequirementCheck.GetChildSpansMissingParent(blocks).Should().BeEmpty();
    }

    [Fact]
    public void GetChildSpansMissingParent_MissingParent_ReturnsOperations()
    {
        var blocks = new List<List<string>>
        {
            new(MakeAgent365Span("chat")),
            new(MakeAgent365Span("execute_tool"))
        };

        var missing = TelemetryRequirementCheck.GetChildSpansMissingParent(blocks);
        missing.Should().Contain("chat");
        missing.Should().Contain("execute_tool");
    }

    [Fact]
    public void GetChildSpansMissingParent_InvokeAgentWithoutParent_IsIgnored()
    {
        var blocks = new List<List<string>>
        {
            new(MakeAgent365Span("invoke_agent"))
        };

        TelemetryRequirementCheck.GetChildSpansMissingParent(blocks)
            .Should().BeEmpty(because: "invoke_agent is a root span and does not need a parent");
    }

    [Fact]
    public void HasNonEmptyValue_ValidValue_ReturnsTrue()
    {
        TelemetryRequirementCheck.HasNonEmptyValue("  parentId: 'abc123'").Should().BeTrue();
    }

    [Fact]
    public void HasNonEmptyValue_Undefined_ReturnsFalse()
    {
        TelemetryRequirementCheck.HasNonEmptyValue("  parentId: undefined").Should().BeFalse();
    }

    [Fact]
    public void HasNonEmptyValue_EmptyQuotes_ReturnsFalse()
    {
        TelemetryRequirementCheck.HasNonEmptyValue("  parentId: ''").Should().BeFalse();
    }

    // --- Resource attribute checks ---

    [Fact]
    public void GetMissingResourceAttributes_AllPresent_ReturnsEmpty()
    {
        var lines = new[]
        {
            "    'telemetry.sdk.name': 'opentelemetry',",
            "    'telemetry.sdk.version': '1.25.0',",
            "    'service.name': 'my-agent',"
        };

        TelemetryRequirementCheck.GetMissingResourceAttributes(lines).Should().BeEmpty();
    }

    [Fact]
    public void GetMissingResourceAttributes_MissingSdkVersion_ReportsIt()
    {
        var lines = new[]
        {
            "    'telemetry.sdk.name': 'opentelemetry',",
            "    'service.name': 'my-agent',"
        };

        var missing = TelemetryRequirementCheck.GetMissingResourceAttributes(lines);
        missing.Should().Contain("telemetry.sdk.version");
        missing.Should().NotContain("telemetry.sdk.name");
        missing.Should().NotContain("service.name");
    }

    [Fact]
    public void GetMissingResourceAttributes_NonePresent_ReturnsAll()
    {
        var lines = new[] { "some unrelated log output" };

        var missing = TelemetryRequirementCheck.GetMissingResourceAttributes(lines);
        missing.Should().HaveCount(3);
    }

    // --- End-to-end: fully compliant spans return success ---

    [Fact]
    public async Task CheckAsync_FullyCompliantSpans_ReturnsSuccess()
    {
        var lines = new List<string>();
        lines.AddRange(ResourceLines);
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("invoke_agent"));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("chat", withParent: true));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("execute_tool", withParent: true));
        lines.Add("}");

        var logPath = CreateTempLogFile(lines.ToArray());
        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue();
        result.IsWarning.Should().BeFalse(because: "fully compliant spans should not produce warnings");
    }

    [Fact]
    public async Task CheckAsync_MissingScopeVersion_ReturnsWarning()
    {
        var lines = new List<string>();
        lines.AddRange(ResourceLines);
        lines.Add("{");
        lines.AddRange(MakeAgent365Span("invoke_agent"));
        lines.Add("}");
        lines.Add("{");
        // chat span without parent or version
        lines.AddRange(MakeAgent365Span("chat"));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeAgent365Span("execute_tool"));
        lines.Add("}");

        var logPath = CreateTempLogFile(lines.ToArray());
        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "scope version missing is a warning not a failure");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("version", because: "warning should mention missing scope version");
    }

    [Fact]
    public async Task CheckAsync_ChildSpansMissingParent_ReturnsWarning()
    {
        var lines = new List<string>();
        lines.AddRange(ResourceLines);
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("invoke_agent"));
        lines.Add("}");
        lines.Add("{");
        // chat without parentId
        lines.AddRange(MakeFullAgent365Span("chat", withParent: false));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("execute_tool", withParent: true));
        lines.Add("}");

        var logPath = CreateTempLogFile(lines.ToArray());
        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "missing parent is a warning not a failure");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("parentId", because: "warning should mention missing parent links");
        result.Details.Should().Contain("chat");
    }

    [Fact]
    public async Task CheckAsync_MissingResourceAttributes_ReturnsWarning()
    {
        var lines = new List<string>();
        // No resource lines
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("invoke_agent"));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("chat", withParent: true));
        lines.Add("}");
        lines.Add("{");
        lines.AddRange(MakeFullAgent365Span("execute_tool", withParent: true));
        lines.Add("}");

        var logPath = CreateTempLogFile(lines.ToArray());
        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "missing resource attributes is a warning not a failure");
        result.IsWarning.Should().BeTrue();
        result.Details.Should().Contain("service.name", because: "warning should list missing resource attributes");
    }
}
