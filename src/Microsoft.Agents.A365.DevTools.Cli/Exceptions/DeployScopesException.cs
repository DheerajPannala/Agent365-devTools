// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.DevTools.Cli.Constants;

namespace Microsoft.Agents.A365.DevTools.Cli.Exceptions;

/// <summary>
/// Exception thrown during deploy scopes operations.
/// </summary>
public class DeployScopesException : Agent365Exception
{
    private const string DeployScopesIssueDescription = "Deploy scopes operation failed";

    public DeployScopesException(string reason, Exception? innerException = null)
        : base(
            errorCode: ErrorCodes.DeploymentScopesFailed,
            issueDescription: DeployScopesIssueDescription,
            errorDetails: new List<string> { reason },
            mitigationSteps: new List<string>
            {
                "Verify tenant and blueprint configuration",
                "Ensure Azure CLI is authenticated and has necessary permissions (e.g. 'az login --tenant <tenant>')",
                "Confirm the blueprint's service principal exists and has propagated in Azure AD; wait a few minutes and retry"
            },
            innerException: innerException)
    {
    }

    public override int ExitCode => 2; // Specific exit code for scopes deployment failures
}
