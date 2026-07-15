// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.Agents.A365.DevTools.Cli.Services;

/// <summary>
/// Sends minimal CLI usage telemetry to Aria (1DS). All operations are best-effort
/// and must never throw or block a command.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Initializes the underlying telemetry system. Safe to call once at startup.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Records a single event with optional string properties.
    /// </summary>
    void TrackEvent(string eventName, IReadOnlyDictionary<string, string>? properties = null);

    /// <summary>
    /// Flushes queued events and shuts the telemetry system down. Call once on exit.
    /// </summary>
    void FlushAndShutdown();
}
