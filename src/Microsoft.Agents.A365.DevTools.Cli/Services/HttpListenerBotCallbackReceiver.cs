// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// HttpListener-based implementation of <see cref="IBotCallbackReceiver"/>.
/// Listens on a random available localhost port for Bot Framework callback activities
/// sent by the agent to POST /v3/conversations/{id}/activities.
/// </summary>
internal sealed class HttpListenerBotCallbackReceiver : IBotCallbackReceiver
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly SemaphoreSlim _responseReceived = new(0);
    private readonly object _lock = new();
    private readonly List<BotCallbackResponse> _responses = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>
    /// After a non-interim callback arrives, continue collecting for this long to capture
    /// any follow-up responses.
    /// </summary>
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Extended timeout used when only interim/acknowledgment responses have been received.
    /// Agents that send "Got it..working on it" often need 10-30s to produce the real response.
    /// </summary>
    internal static readonly TimeSpan InterimExtendedTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Patterns that indicate an interim/acknowledgment message rather than a real response.
    /// These are anchored to avoid matching error messages (e.g. "processing failed" is NOT interim).
    /// </summary>
    internal static readonly string[] InterimPatterns = new[]
    {
        "got it",
        "working on",
        "work on it",
        "processing your",
        "thinking",
        "one moment",
        "please wait",
        "hold on",
        "looking into",
        "let me check",
        "let me look",
        "let me find",
        "let me get",
        "just a moment",
        "just a sec",
    };

    public string ServiceUrl => $"http://localhost:{_port}";

    public HttpListenerBotCallbackReceiver()
    {
        _port = FindAvailablePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    /// <summary>
    /// Initializes with a specific port (for testing).
    /// </summary>
    internal HttpListenerBotCallbackReceiver(int port)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task<BotCallbackResponse?> WaitForResponseAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        bool receivedInterim = false;

        try
        {
            // Wait for the first callback activity
            await _responseReceived.WaitAsync(timeoutCts.Token);

            // Check if the first response is an interim message
            bool firstIsFinal;
            lock (_lock)
            {
                var latest = _responses.Count > 0 ? _responses[^1] : null;
                firstIsFinal = latest is not null && IsFinalMessage(latest);
                if (!firstIsFinal)
                {
                    receivedInterim = true;
                }
            }

            if (receivedInterim)
            {
                // Interim message received — agent is alive but still processing.
                // Extend the timeout to allow the real response to arrive.
                var extendedDeadline = DateTime.UtcNow + InterimExtendedTimeout;

                while (DateTime.UtcNow < extendedDeadline && !timeoutCts.Token.IsCancellationRequested)
                {
                    var remaining = extendedDeadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    try
                    {
                        if (!await _responseReceived.WaitAsync(remaining, timeoutCts.Token))
                        {
                            break;
                        }

                        // Check if we now have a final response
                        lock (_lock)
                        {
                            var latest = _responses.Count > 0 ? _responses[^1] : null;
                            if (latest is not null && IsFinalMessage(latest))
                            {
                                // Got a real response — start the short grace period for any follow-ups
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            // Grace period: collect any remaining follow-up responses
            var graceDeadline = DateTime.UtcNow + GracePeriod;

            while (DateTime.UtcNow < graceDeadline && !timeoutCts.Token.IsCancellationRequested)
            {
                var remaining = graceDeadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    if (!await _responseReceived.WaitAsync(remaining, timeoutCts.Token))
                    {
                        break; // No more responses within grace period
                    }
                    // Got another response — continue collecting
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Overall timeout or cancellation — return whatever we collected
        }

        lock (_lock)
        {
            return SelectBestResponse();
        }
    }

    public void ClearResponses()
    {
        lock (_lock)
        {
            _responses.Clear();
        }

        while (_responseReceived.Wait(0))
        {
            // Drain any pending signals
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            // Bot Framework sends responses to POST /v3/conversations/{id}/activities[/{id}]
            if (context.Request.HttpMethod == "POST" &&
                context.Request.Url?.AbsolutePath.Contains("/v3/conversations/", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var text = doc.RootElement.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                    var type = doc.RootElement.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                    lock (_lock)
                    {
                        _responses.Add(new BotCallbackResponse(text, type));
                    }

                    _responseReceived.Release();
                }
                catch (JsonException)
                {
                    lock (_lock)
                    {
                        _responses.Add(new BotCallbackResponse(null, null));
                    }

                    _responseReceived.Release();
                }
            }

            // Return 200 OK with a ResourceResponse (required by Bot Framework SDK)
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.SerializeToUtf8Bytes(new { id = Guid.NewGuid().ToString("N") });
            await context.Response.OutputStream.WriteAsync(responseJson);
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Selects the best response from collected callbacks.
    /// Returns null if only interim/typing responses were collected (agent did not produce a final answer).
    /// Prefers the last message-type response with substantive non-interim text.
    /// </summary>
    private BotCallbackResponse? SelectBestResponse()
    {
        if (_responses.Count == 0)
        {
            return null;
        }

        // Prefer the last final message (non-interim, non-typing, with substantive text)
        var bestMessage = _responses
            .LastOrDefault(r => IsFinalMessage(r));

        if (bestMessage is not null)
        {
            return bestMessage;
        }

        // No final message found — all responses were interim or typing.
        // Return null so the caller treats this as "agent did not respond with a final answer".
        return null;
    }

    /// <summary>
    /// Determines whether a response is a final (non-interim) message from the agent.
    /// </summary>
    internal static bool IsFinalMessage(BotCallbackResponse response)
    {
        // Typing activities are never final
        if (string.Equals(response.Type, "typing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Must be a message with text
        if (response.Type != "message" || string.IsNullOrWhiteSpace(response.Text))
        {
            return false;
        }

        return !IsInterimMessage(response.Text);
    }

    /// <summary>
    /// Detects interim/acknowledgment messages that agents send while processing.
    /// Only matches short messages (under 60 chars) containing known interim phrases.
    /// Longer messages are assumed to be real responses even if they contain interim-like words.
    /// </summary>
    internal static bool IsInterimMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Long messages are unlikely to be interim acknowledgments
        if (text.Length > 60)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();

        foreach (var pattern in InterimPatterns)
        {
            if (lower.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        try { _listener.Stop(); }
        catch { /* best effort */ }

        if (_listenTask is not null)
        {
            try { await _listenTask; }
            catch { /* best effort */ }
        }

        try { _listener.Close(); }
        catch { /* best effort */ }

        _cts?.Dispose();
        _responseReceived.Dispose();
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
