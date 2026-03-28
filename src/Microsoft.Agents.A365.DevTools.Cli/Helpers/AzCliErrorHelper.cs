// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.DevTools.Cli.Helpers;

/// <summary>
/// Helpers for matching Azure CLI / ARM error codes in a locale-independent manner.
/// Azure CLI outputs errors in two formats:
///   1. Plain text: ERROR: (ErrorCode) Localized message...
///   2. JSON: {"error": {"code": "ErrorCode", "message": "..."}}
/// ARM error codes (e.g. AuthorizationFailed, Conflict, QuotaExceeded) are never
/// translated, unlike human-readable messages which vary by Azure CLI language.
/// </summary>
internal static class AzCliErrorHelper
{
    /// <summary>
    /// Checks whether the Azure CLI stderr output contains the specified ARM error code.
    /// Matches against the error code token (never localized) rather than message text.
    /// </summary>
    internal static bool ContainsErrorCode(string? stderr, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        return stderr.Contains(errorCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the Azure CLI stderr output contains any of the specified ARM error codes.
    /// </summary>
    internal static bool ContainsAnyErrorCode(string? stderr, params string[] errorCodes)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        return errorCodes.Any(code => ContainsErrorCode(stderr, code));
    }
}
