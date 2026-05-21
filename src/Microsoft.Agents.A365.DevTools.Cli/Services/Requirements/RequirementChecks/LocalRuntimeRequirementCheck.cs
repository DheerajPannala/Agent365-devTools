// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.Agents.A365.DevTools.Cli.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services.Requirements.RequirementChecks;

/// <summary>
/// Validates that the user's agent app starts locally and responds on a health endpoint.
/// Spawns the app process, polls /api/health, captures stdout/stderr, then stops the process.
/// </summary>
public class LocalRuntimeRequirementCheck : RequirementCheck
{
    private readonly PlatformDetector _platformDetector;
    private readonly IProcessService _processService;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Default port used when no port can be inferred from configuration.
    /// </summary>
    internal const int DefaultPort = 5000;

    /// <summary>
    /// Default health endpoint path to probe.
    /// </summary>
    internal const string DefaultHealthPath = "/api/health";

    /// <summary>
    /// Maximum time to wait for the app to start and respond.
    /// </summary>
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between health endpoint polls.
    /// </summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum number of stdout/stderr lines to capture for diagnostics.
    /// </summary>
    internal const int MaxOutputLines = 50;

    public LocalRuntimeRequirementCheck(
        PlatformDetector platformDetector,
        IProcessService processService,
        HttpClient? httpClient = null)
    {
        _platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public override string Name => "Local Runtime";

    /// <inheritdoc />
    public override string Description => "Validates that the agent app starts locally and responds on a health endpoint";

    /// <inheritdoc />
    public override string Category => "Code Health";

    /// <inheritdoc />
    public override async Task<RequirementCheckResult> CheckAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCheckWithLoggingAsync(config, logger, CheckImplementationAsync, cancellationToken);
    }

    private async Task<RequirementCheckResult> CheckImplementationAsync(
        Agent365Config config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var projectPath = ResolveProjectPath(config);

        if (!Directory.Exists(projectPath))
        {
            return RequirementCheckResult.Failure(
                $"Project path does not exist: {projectPath}",
                "Ensure the project directory exists, or set deploymentProjectPath in a365.config.json");
        }

        var platform = _platformDetector.Detect(projectPath);
        if (platform == ProjectPlatform.Unknown)
        {
            return RequirementCheckResult.Warning(
                "Could not detect project platform, skipping local runtime validation",
                details: $"No .NET, Node.js, or Python project detected in {projectPath}");
        }

        var port = ResolvePort(config.MessagingEndpoint);
        var healthUrl = $"http://localhost:{port}{DefaultHealthPath}";

        logger.LogDebug(
            "Starting local runtime check: platform={Platform}, port={Port}, healthUrl={HealthUrl}, projectPath={ProjectPath}",
            platform, port, healthUrl, projectPath);

        var startInfo = BuildProcessStartInfo(platform, projectPath, port);
        return await SpawnAndProbeAsync(startInfo, healthUrl, platform, port, logger, cancellationToken);
    }

    /// <summary>
    /// Resolves the local port from a MessagingEndpoint URL. Only uses the port when the host
    /// is localhost/127.0.0.1/[::1]. Otherwise returns the default port.
    /// </summary>
    internal static int ResolvePort(string? messagingEndpoint)
    {
        if (string.IsNullOrWhiteSpace(messagingEndpoint))
        {
            return DefaultPort;
        }

        if (Uri.TryCreate(messagingEndpoint, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            var isLocalhost = host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

            if (isLocalhost && !uri.IsDefaultPort)
            {
                return uri.Port;
            }
        }

        return DefaultPort;
    }

    private static ProcessStartInfo BuildProcessStartInfo(ProjectPlatform platform, string projectPath, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        switch (platform)
        {
            case ProjectPlatform.DotNet:
                startInfo.FileName = "dotnet";
                startInfo.Arguments = "run --no-build";
                startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://localhost:{port}";
                break;

            case ProjectPlatform.NodeJs:
                startInfo.FileName = "npm";
                startInfo.Arguments = "start";
                startInfo.EnvironmentVariables["PORT"] = port.ToString();
                break;

            case ProjectPlatform.Python:
                startInfo.FileName = "python";
                startInfo.Arguments = "app.py";
                startInfo.EnvironmentVariables["PORT"] = port.ToString();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform");
        }

        return startInfo;
    }

    private async Task<RequirementCheckResult> SpawnAndProbeAsync(
        ProcessStartInfo startInfo,
        string healthUrl,
        ProjectPlatform platform,
        int port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var outputLines = new BoundedLineBuffer(MaxOutputLines);
        var errorLines = new BoundedLineBuffer(MaxOutputLines);
        Process? process = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            process = _processService.Start(startInfo);
            if (process is null)
            {
                return RequirementCheckResult.Failure(
                    $"Failed to start {platform} process",
                    GetRunGuidance(platform));
            }

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null) outputLines.Add(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null) errorLines.Add(args.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(StartupTimeout);

            while (!timeoutCts.Token.IsCancellationRequested)
            {
                if (process.HasExited)
                {
                    var exitOutput = GetCapturedOutput(outputLines, errorLines);
                    return RequirementCheckResult.Failure(
                        $"App exited early with code {process.ExitCode} before health endpoint responded:\n{exitOutput}",
                        GetRunGuidance(platform));
                }

                try
                {
                    using var response = await _httpClient.GetAsync(healthUrl, timeoutCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        stopwatch.Stop();
                        logger.LogDebug("Health endpoint returned {StatusCode}", (int)response.StatusCode);
                        return new RequirementCheckResult
                        {
                            Passed = true,
                            Details = $"{platform} app running on port {port}, health endpoint returned HTTP {(int)response.StatusCode}",
                            Metadata = new RequirementCheckMetadata
                            {
                                Port = port,
                                BootMs = stopwatch.ElapsedMilliseconds,
                                Platform = platform.ToString()
                            }
                        };
                    }

                    logger.LogDebug("Health endpoint returned non-success status {StatusCode}", (int)response.StatusCode);
                }
                catch (HttpRequestException)
                {
                    // App not ready yet, will retry
                }
                catch (TaskCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Timeout — fall through to failure below
                    break;
                }

                try
                {
                    await Task.Delay(PollInterval, timeoutCts.Token);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var timeoutOutput = GetCapturedOutput(outputLines, errorLines);
            return RequirementCheckResult.Failure(
                $"App did not respond on {healthUrl} within {(int)StartupTimeout.TotalSeconds} seconds:\n{timeoutOutput}",
                GetRunGuidance(platform));
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to kill process during cleanup");
                }

                process.Dispose();
            }
        }
    }

    private static string GetCapturedOutput(BoundedLineBuffer outputLines, BoundedLineBuffer errorLines)
    {
        var sb = new StringBuilder();
        var stdout = outputLines.GetLines();
        var stderr = errorLines.GetLines();

        if (stdout.Length > 0)
        {
            sb.AppendLine("  [stdout]");
            foreach (var line in stdout)
                sb.AppendLine($"    {line}");
        }

        if (stderr.Length > 0)
        {
            sb.AppendLine("  [stderr]");
            foreach (var line in stderr)
                sb.AppendLine($"    {line}");
        }

        if (sb.Length == 0)
        {
            sb.Append("  (no output captured)");
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetRunGuidance(ProjectPlatform platform)
    {
        return platform switch
        {
            ProjectPlatform.DotNet => "Try running the app manually:\n" +
                "  dotnet run\n" +
                "Verify it starts and exposes /api/health.",
            ProjectPlatform.NodeJs => "Try running the app manually:\n" +
                "  npm start\n" +
                "Verify it starts and exposes /api/health.",
            ProjectPlatform.Python => "Try running the app manually:\n" +
                "  python app.py\n" +
                "Verify it starts and exposes /api/health.",
            _ => "Try running the app manually and verify it exposes /api/health."
        };
    }

    /// <summary>
    /// Thread-safe bounded buffer that keeps the last N lines.
    /// </summary>
    internal sealed class BoundedLineBuffer
    {
        private readonly Queue<string> _lines;
        private readonly int _maxLines;
        private readonly object _lock = new();

        public BoundedLineBuffer(int maxLines)
        {
            _maxLines = maxLines;
            _lines = new Queue<string>(maxLines);
        }

        public void Add(string line)
        {
            lock (_lock)
            {
                if (_lines.Count >= _maxLines)
                {
                    _lines.Dequeue();
                }
                _lines.Enqueue(line);
            }
        }

        public string[] GetLines()
        {
            lock (_lock)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <summary>
    /// Returns deploymentProjectPath if configured, otherwise falls back to the current directory.
    /// </summary>
    private static string ResolveProjectPath(Agent365Config config)
    {
        return string.IsNullOrWhiteSpace(config.DeploymentProjectPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(config.DeploymentProjectPath);
    }
}
