// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using DDT.Contracts.Deployments;
using DDT.Server.Settings;

namespace DDT.Server.Deployments;

// Asks the domain, as the join account, whether a Join the domain step would get its computer account, before a machine
// finds out an hour into its run. The settings are read on every check, never kept, so a check after they change on the
// settings page uses the new ones.
public sealed class DomainJoinCheck(IDomainDirectory directory, DdtSettings settings, TimeProvider timeProvider)
{
    public async Task<DomainJoinCheckView> RunAsync(string? organizationalUnit, CancellationToken cancellationToken)
    {
        DomainOptions domain = settings.Current.Deployment.Domain;
        DateTimeOffset now = timeProvider.GetUtcNow();
        string? name = Value(domain.Name);
        string? userName = Value(domain.UserName);

        if (name is null)
        {
            return Stopped(now, null, null, null, "No domain is configured. Set DDT:Deployment:Domain:Name and the join account on the server.");
        }

        string controller = Value(domain.Controller) ?? name;

        if (userName is null || string.IsNullOrEmpty(domain.Password))
        {
            return Stopped(now, name, userName, controller, "The join account is not configured. Set DDT:Deployment:Domain:UserName and Password on the server.");
        }

        string? unit = Value(organizationalUnit) ?? Value(domain.OrganizationalUnit);

        if (unit is not null && DeploymentOptionsValidation.OrganizationalUnitProblem(unit) is { } unitProblem)
        {
            return Stopped(now, name, userName, controller, unitProblem);
        }

        DomainDirectoryFacts facts;

        try
        {
            facts = await directory
                .ReadAsync(new DomainDirectoryRequest(controller, userName, domain.Password, unit), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DomainDirectoryException exception)
        {
            return Stopped(now, name, userName, controller, exception.Failure switch
            {
                DomainDirectoryFailure.SignInRefused => DomainJoinAssessment.DescribeRefusal(exception.Detail, controller, userName),
                DomainDirectoryFailure.NoSecureConnection => DomainJoinAssessment.DescribeNoSecureConnection(controller),
                _ => DomainJoinAssessment.DescribeUnreachable(controller, name, exception.Detail),
            });
        }
        catch (DirectoryException exception)
        {
            return Stopped(now, name, userName, controller, $"{controller} answered the check with an error: {exception.Message}");
        }

        DomainJoinAssessment.Verdict verdict = DomainJoinAssessment.Assess(name, userName, controller, unit, facts);

        return new DomainJoinCheckView(verdict.CanJoin, name, userName, controller, verdict.Container, verdict.Findings, now);
    }

    private static DomainJoinCheckView Stopped(DateTimeOffset now, string? domain, string? userName, string? controller, string problem) =>
        new(false, domain, userName, controller, null, [new DomainJoinFinding(DomainJoinFindingLevel.Problem, problem)], now);

    private static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
