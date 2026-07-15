// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.Agents.A365.DevTools.Cli.Constants;
using Microsoft.Applications.Telemetry;
using Microsoft.Applications.Telemetry.Server;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// Aria (1DS) implementation of <see cref="ITelemetryService"/>. Wraps LogManager so the
/// rest of the CLI depends only on the interface. Every call is guarded so telemetry
/// failures are silent and never affect command execution.
/// </summary>
public sealed class TelemetryService : ITelemetryService
{
    private const string EventSource = "a365cli";

    private readonly ILogger<TelemetryService> _logger;
    private string? _token;
    private bool _initialized;

    public TelemetryService(ILogger<TelemetryService> logger)
    {
        _logger = logger;
    }

    private static bool IsOptedOut()
    {
        var value = Environment.GetEnvironmentVariable(ConfigConstants.TelemetryOptOutEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void Initialize()
    {
        if (_initialized || IsOptedOut())
        {
            return;
        }

        try
        {
            var token = Environment.GetEnvironmentVariable(ConfigConstants.TelemetryTokenEnvVar);
            if (string.IsNullOrWhiteSpace(token))
            {
                token = ConfigConstants.TelemetryAppToken;
            }

            _token = token;
            LogManager.Initialize(token);
            _initialized = true;
            _logger.LogDebug("Telemetry initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry initialization failed: {Message}", ex.Message);
        }
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        try
        {
            var eventData = new EventProperties(eventName);
            if (properties is not null)
            {
                foreach (var kvp in properties)
                {
                    eventData.SetProperty(kvp.Key, kvp.Value ?? string.Empty);
                }
            }

            LogManager.GetLogger(_token, EventSource).LogEvent(eventData);
            _logger.LogDebug("Telemetry event '{EventName}' queued.", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry event '{EventName}' failed: {Message}", eventName, ex.Message);
        }
    }

    public void FlushAndShutdown()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            LogManager.FlushAndTearDown(BehaviorAfterDispose.SilentlyIgnoreExceptionsAfterDispose);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry flush failed: {Message}", ex.Message);
        }
        finally
        {
            _token = null;
            _initialized = false;
        }
    }
}
