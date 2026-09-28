// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Server.Settings;
using Microsoft.Extensions.Options;

namespace DDT.Server.Deployments;

// Asks the domain, as the join account, whether a Join the domain step would get its computer account, before a machine
// finds out an hour into its run. The settings are read on every check, so a change counts at once.
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
            return Stopped(now, null, null, null, ServerMessages.DomainNotConfigured.With());
        }

        string controller = Value(domain.Controller) ?? name;

        if (userName is null || string.IsNullOrEmpty(domain.Password))
        {
            return Stopped(now, name, userName, controller, ServerMessages.DomainNoJoinAccount.With());
        }

        string? unit = Value(organizationalUnit) ?? Value(domain.OrganizationalUnit);

        if (unit is not null && DeploymentOptionsValidation.OrganizationalUnitMessage(unit) is { } unitProblem)
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
                DomainDirectoryFailure.SignInRefused => DomainJoinAssessment.RefusalMessage(exception.Detail, controller, userName),
                DomainDirectoryFailure.NoSecureConnection => DomainJoinAssessment.NoSecureConnectionMessage(controller),
                _ => DomainJoinAssessment.UnreachableMessage(controller, name, exception.Detail),
            });
        }
        catch (DirectoryException exception)
        {
            return Stopped(now, name, userName, controller, ServerMessages.DomainCheckError.With("controller", controller, "detail", exception.Message));
        }

        DomainJoinVerdict verdict = DomainJoinAssessment.Assess(name, userName, controller, unit, facts);

        return new DomainJoinCheckView(verdict.CanJoin, name, userName, controller, verdict.Container, verdict.Findings, now);
    }

    private static DomainJoinCheckView Stopped(DateTimeOffset now, string? domain, string? userName, string? controller, ServerMessage problem) =>
        new(false, domain, userName, controller, null, [DomainJoinFinding.From(DomainJoinFindingLevel.Problem, problem)], now);

    private static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
