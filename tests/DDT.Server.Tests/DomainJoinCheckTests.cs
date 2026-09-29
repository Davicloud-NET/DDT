// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Deployments;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DomainJoinCheckTests(DomainJoinCheckApplication application) : IClassFixture<DomainJoinCheckApplication>
{
    private const string Path = "/api/deployments/domain-check";

    private async Task<DomainJoinCheckView> CheckAsync(string? organizationalUnit)
    {
        HttpResponseMessage response = await (await application.AdministratorAsync()).PostAsync(Path, new DomainJoinCheckRequest(organizationalUnit));

        return await RegisteredMachine.ReadAsync<DomainJoinCheckView>(response);
    }

    private Task<List<AuditEvent>> AuditAsync() =>
        application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.Action == AuditActions.DomainJoinChecked)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task AsksTheNamedControllerWithTheJoinAccountAndTheStepsOrganizationalUnit()
    {
        DomainJoinCheckView result = await CheckAsync("OU=Kiosks,DC=corp,DC=example");

        Assert.True(result.CanJoin);
        Assert.Equal(("corp.example", @"CORP\ddt-join", "dc1.corp.example", "OU=Kiosks,DC=corp,DC=example"), (result.Domain, result.UserName, result.Controller, result.Container));
        Assert.Equal(
            new DomainDirectoryRequest("dc1.corp.example", @"CORP\ddt-join", DomainDeploymentApplication.JoinPassword, "OU=Kiosks,DC=corp,DC=example"),
            application.Directory.Requests[^1]);
    }

    [Fact]
    public async Task WithoutAnOrganizationalUnitOfItsOwnTheStepTakesTheConfiguredOne()
    {
        await CheckAsync("  ");

        Assert.Equal("OU=Workstations,DC=corp,DC=example", application.Directory.Requests[^1].OrganizationalUnit);
    }

    [Fact]
    public async Task AnOrganizationalUnitThatIsNoDistinguishedNameIsRefusedBeforeTheDomainIsAsked()
    {
        int asked = application.Directory.Requests.Count;

        DomainJoinCheckView result = await CheckAsync("Workstations");

        Assert.False(result.CanJoin);
        Assert.Contains("is not a distinguished name", Assert.Single(result.Findings).Text, StringComparison.Ordinal);
        Assert.Equal(asked, application.Directory.Requests.Count);
    }

    [Fact]
    public async Task ARefusedPasswordSaysSoAndNeverShowsIt()
    {
        application.Directory.Answer = _ => throw new DomainDirectoryException(DomainDirectoryFailure.SignInRefused, "52e");

        try
        {
            DomainJoinCheckView result = await CheckAsync(null);

            Assert.False(result.CanJoin);
            DomainJoinFinding finding = Assert.Single(result.Findings);
            Assert.Equal(DomainJoinFindingLevel.Problem, finding.Level);
            Assert.Equal(@"dc1.corp.example did not accept the password of CORP\ddt-join. Correct the join account password on the Deployment defaults page.", finding.Text);

            AuditEvent audit = (await AuditAsync())[^1];
            Assert.Equal("corp.example", audit.SubjectId);
            Assert.Contains("it cannot", audit.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(DomainDeploymentApplication.JoinPassword, audit.Detail, StringComparison.Ordinal);
        }
        finally
        {
            application.Directory.Answer = new FakeDomainDirectory().Answer;
        }
    }

    [Fact]
    public async Task AnUnreachableControllerPointsAtTheControllerSetting()
    {
        application.Directory.Answer = _ => throw new DomainDirectoryException(DomainDirectoryFailure.Unreachable, "The LDAP server is unavailable.");

        try
        {
            DomainJoinCheckView result = await CheckAsync(null);

            Assert.Contains("dc1.corp.example could not be reached over LDAP (The LDAP server is unavailable)", result.Findings[0].Text, StringComparison.Ordinal);
            Assert.Contains("for the check on the Deployment defaults page", result.Findings[0].Text, StringComparison.Ordinal);
        }
        finally
        {
            application.Directory.Answer = new FakeDomainDirectory().Answer;
        }
    }

    [Fact]
    public async Task EveryCheckIsAudited()
    {
        await CheckAsync(null);

        AuditEvent audit = (await AuditAsync())[^1];
        Assert.Equal(
            @"Checked whether CORP\ddt-join can join corp.example at dc1.corp.example: it can. CORP\ddt-join may create computer objects in " +
            "OU=Workstations,DC=corp,DC=example, as many as it needs.",
            audit.Detail);
    }

    [Theory]
    [InlineData(DdtRoleNames.Operator)]
    [InlineData(DdtRoleNames.Viewer)]
    public async Task OnlyAdministratorsMayCheck(string role)
    {
        using SignedInClient client = await application.SignInAsync(role);

        HttpResponseMessage response = await client.PostAsync(Path, new DomainJoinCheckRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
