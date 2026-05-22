// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Agents.A365.DevTools.Cli.Services.Requirements;
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

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { /* best-effort cleanup */ }
        }
    }

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

    [Fact]
    public async Task CheckAsync_NullLogPath_ReturnsWarning()
    {
        var check = new TelemetryRequirementCheck(null);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "no log file is a warning, not a failure");
        result.IsWarning.Should().BeTrue(because: "missing log file means telemetry status is unknown");
        result.ErrorMessage.Should().Contain("No agent console log file available");
    }

    [Fact]
    public async Task CheckAsync_NonExistentLogPath_ReturnsWarning()
    {
        var check = new TelemetryRequirementCheck("/nonexistent/path.log");

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "missing file is a warning, not a failure");
        result.IsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_NoTelemetryLines_ReturnsWarning()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "info: Listening on http://localhost:5000",
            "info: Received message: Hello"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "no telemetry evidence means telemetry is not configured");
        result.IsWarning.Should().BeFalse(because: "missing telemetry is a failure, not a warning");
        result.ErrorMessage.Should().Contain("No telemetry-related output detected");
    }

    [Fact]
    public async Task CheckAsync_SuccessPatterns_ReturnsPass()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "info: OpenTelemetry TracerProvider built successfully",
            "info: OtlpExporter configured for https://agent365.observability.endpoint",
            "info: BatchExportProcessor started",
            "info: Export completed - 5 spans exported"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "success patterns indicate traces are being exported");
        result.IsWarning.Should().BeFalse();
        result.Details.Should().Contain("Telemetry export evidence found");
    }

    [Fact]
    public async Task CheckAsync_FailurePatterns_ReturnsFail()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "info: OpenTelemetry TracerProvider built successfully",
            "error: OTLP export failed: connection refused",
            "warn: Dropped spans due to exporter error"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "export failures indicate telemetry is not working");
        result.ErrorMessage.Should().Contain("Telemetry export failures detected");
        result.ResolutionGuidance.Should().Contain("OTLP endpoint", because: "connection refused should suggest checking endpoint connectivity");
    }

    [Fact]
    public async Task CheckAsync_MixedSuccessAndFailure_FailureTakesPrecedence()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: OpenTelemetry TracerProvider built successfully",
            "info: Export completed - 3 spans exported",
            "error: OTLP exporter: UNAVAILABLE - endpoint unreachable",
            "info: BatchExportProcessor: dropped spans"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "failure patterns take precedence over success patterns");
        result.Details.Should().Contain("Failure patterns found");
        result.Details.Should().Contain("Success patterns found");
    }

    [Fact]
    public async Task CheckAsync_TelemetryContextButNoExportEvidence_ReturnsWarning()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "dbug: OpenTelemetry SDK initialized",
            "dbug: Adding OTLP exporter to pipeline"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "SDK detected without export evidence should fail");
        result.IsWarning.Should().BeFalse(because: "no confirmed export means telemetry is not working");
        result.ErrorMessage.Should().Contain("Telemetry SDK detected but no trace export evidence found");
    }

    [Fact]
    public async Task CheckAsync_Agent365ObservabilityPattern_ReturnsPass()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Configuring Agent365.Observability.OtelWrite endpoint",
            "info: TracerProvider started with OTLP exporter"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue();
        result.IsWarning.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_CaseInsensitiveMatching()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "INFO: OPENTELEMETRY TRACERPROVIDER BUILT successfully",
            "INFO: OTLPEXPORTER EXPORT COMPLETED"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "pattern matching should be case-insensitive");
        result.IsWarning.Should().BeFalse();
    }

    [Fact]
    public void FilterTelemetryLines_FiltersOnlyRelevantLines()
    {
        var logLines = new[]
        {
            "info: Application started",
            "info: OpenTelemetry TracerProvider built",
            "info: Listening on port 5000",
            "error: OTLP export failed",
            "info: Received message"
        };

        var result = TelemetryRequirementCheck.FilterTelemetryLines(logLines);

        result.Should().HaveCount(2);
        result[0].Should().Contain("OpenTelemetry");
        result[1].Should().Contain("OTLP");
    }

    [Fact]
    public void FilterTelemetryLines_RespectsMaxLimit()
    {
        var logLines = Enumerable.Range(0, 200)
            .Select(i => $"info: OpenTelemetry span {i} exported")
            .ToArray();

        var result = TelemetryRequirementCheck.FilterTelemetryLines(logLines);

        result.Should().HaveCount(TelemetryRequirementCheck.MaxTelemetryLines);
    }

    [Fact]
    public void FilterTelemetryLines_SkipsEmptyAndWhitespace()
    {
        var logLines = new[] { "", "  ", null!, "info: TracerProvider started" };

        var result = TelemetryRequirementCheck.FilterTelemetryLines(logLines);

        result.Should().ContainSingle()
            .Which.Should().Contain("TracerProvider");
    }

    [Fact]
    public void FindMatchingPatterns_FindsAllMatches()
    {
        var lines = new List<string>
        {
            "Export completed successfully",
            "TracerProvider built and started",
            "OtlpExporter configured"
        };

        var result = TelemetryRequirementCheck.FindMatchingPatterns(
            lines, TelemetryRequirementCheck.SuccessPatterns);

        result.Should().Contain("export completed");
        result.Should().Contain("otlpexporter");
    }

    [Fact]
    public void FindMatchingPatterns_ReturnsEmptyForNoMatches()
    {
        var lines = new List<string>
        {
            "Application started",
            "Listening on port 5000"
        };

        var result = TelemetryRequirementCheck.FindMatchingPatterns(
            lines, TelemetryRequirementCheck.SuccessPatterns);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_UnrelatedConnectionRefused_NotFlaggedAsFailure()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "info: Application started",
            "error: Database connection refused on port 5432"
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeTrue(because: "unrelated connection errors should not trigger telemetry failure");
        result.IsWarning.Should().BeTrue(because: "no telemetry-relevant lines found");
    }

    [Fact]
    public async Task CheckAsync_Agent365ExporterSpansSkipped_ReturnsFail()
    {
        var logPath = CreateTempLogFile(new[]
        {
            "dbug: Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365Exporter[0]",
            "      Agent365Exporter: Exporting batch of 7 spans.",
            "dbug: Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterCore[0]",
            "      [Agent365Exporter] 5 non-genAI spans filtered out",
            "dbug: Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterCore[0]",
            "      [Agent365Exporter] 2 spans skipped due to missing tenant or agent ID",
            "dbug: Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterCore[0]",
            "      [Agent365Exporter] Partitioned into 0 identity groups (7 spans skipped)",
            "dbug: Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365Exporter[0]",
            "      Agent365Exporter: No spans with tenant/agent identity found; nothing exported."
        });

        var check = new TelemetryRequirementCheck(logPath);

        var result = await check.CheckAsync(_config, _logger);

        result.Passed.Should().BeFalse(because: "Agent365Exporter skipped all spans due to missing identity -- telemetry is not working");
        result.ErrorMessage.Should().Contain("Telemetry export failures detected");
        result.ResolutionGuidance.Should().Contain("tenant ID", because: "missing tenant/agent ID requires identity configuration guidance");
    }
}
