// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Helpers;
using Xunit;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Helpers;

public class AzCliErrorHelperTests
{
    [Theory]
    [InlineData("ERROR: (AuthorizationFailed) The client does not have authorization", "AuthorizationFailed", true)]
    [InlineData("ERROR: (QuotaExceeded) Operation cannot be completed", "QuotaExceeded", true)]
    [InlineData("ERROR: (Conflict) Web app already exists", "Conflict", true)]
    [InlineData("ERROR: (InvalidSku) The requested SKU is invalid", "InvalidSku", true)]
    [InlineData("ERROR: (SkuNotAvailable) The SKU is not available", "SkuNotAvailable", true)]
    [InlineData("{\"error\": {\"code\": \"AuthorizationFailed\", \"message\": \"...\"}}", "AuthorizationFailed", true)]
    public void ContainsErrorCode_WhenErrorCodePresent_ReturnsTrue(string stderr, string errorCode, bool expected)
    {
        AzCliErrorHelper.ContainsErrorCode(stderr, errorCode).Should().Be(expected,
            because: "ARM error codes are language-independent tokens that appear in the Azure CLI output");
    }

    [Theory]
    [InlineData(null, "AuthorizationFailed")]
    [InlineData("", "AuthorizationFailed")]
    [InlineData("   ", "AuthorizationFailed")]
    public void ContainsErrorCode_WhenStderrNullOrEmpty_ReturnsFalse(string? stderr, string errorCode)
    {
        AzCliErrorHelper.ContainsErrorCode(stderr, errorCode).Should().BeFalse();
    }

    [Theory]
    [InlineData("Der Client hat keine Berechtigung diese Aktion auszufuehren", "AuthorizationFailed",
        "because non-English localized messages should not match when the error code is absent")]
    [InlineData("Le client n'a pas l'autorisation d'effectuer cette action", "AuthorizationFailed",
        "because French localized messages should not match when the error code is absent")]
    [InlineData("Kontingent ueberschritten. Bitte erhoehen Sie das Kontingent.", "QuotaExceeded",
        "because German quota message should not match when the QuotaExceeded error code is absent")]
    public void ContainsErrorCode_WhenOnlyLocalizedTextPresent_ReturnsFalse(string stderr, string errorCode, string because)
    {
        AzCliErrorHelper.ContainsErrorCode(stderr, errorCode).Should().BeFalse(because);
    }

    [Theory]
    [InlineData("ERROR: (AuthorizationFailed) Der Client hat keine Berechtigung", new[] { "AuthorizationFailed", "LinkedAuthorizationFailed" }, true)]
    [InlineData("ERROR: (LinkedAuthorizationFailed) Something", new[] { "AuthorizationFailed", "LinkedAuthorizationFailed" }, true)]
    [InlineData("ERROR: (SomeOtherError) Something", new[] { "AuthorizationFailed", "LinkedAuthorizationFailed" }, false)]
    public void ContainsAnyErrorCode_MatchesAnyOfTheGivenCodes(string stderr, string[] errorCodes, bool expected)
    {
        AzCliErrorHelper.ContainsAnyErrorCode(stderr, errorCodes).Should().Be(expected);
    }

    [Fact]
    public void ContainsAnyErrorCode_WhenStderrNull_ReturnsFalse()
    {
        AzCliErrorHelper.ContainsAnyErrorCode(null, "AuthorizationFailed", "Conflict").Should().BeFalse();
    }
}
