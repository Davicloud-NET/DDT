// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Deployments;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The join account of DomainDeploymentApplication, checked at a named domain controller that a fake answers for.
public sealed class DomainJoinCheckApplication() : SettingsApplication(
    ("DDT:Deployment:LocalAdministrator:Password", DomainDeploymentApplication.AdministratorPassword),
    ("DDT:Deployment:Domain:Name", "corp.example"),
    ("DDT:Deployment:Domain:OrganizationalUnit", "OU=Workstations,DC=corp,DC=example"),
    ("DDT:Deployment:Domain:UserName", @"CORP\ddt-join"),
    ("DDT:Deployment:Domain:Password", DomainDeploymentApplication.JoinPassword),
    ("DDT:Deployment:Domain:Controller", "dc1.corp.example"))
{
    public FakeDomainDirectory Directory { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        base.ConfigureTestHost(builder);

        builder.ConfigureServices(services => services.AddSingleton<IDomainDirectory>(Directory));
    }
}

public sealed class FakeDomainDirectory : IDomainDirectory
{
    private readonly List<DomainDirectoryRequest> _requests = [];

    public Func<DomainDirectoryRequest, DomainDirectoryFacts> Answer { get; set; } =
        request => new("LDAPS", "DC=corp,DC=example", request.OrganizationalUnit, CanCreateComputers: true, 10, 0);

    public IReadOnlyList<DomainDirectoryRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<DomainDirectoryFacts> ReadAsync(DomainDirectoryRequest request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(Answer(request));
    }
}

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

public sealed class DomainJoinCheckWithoutDomainTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task WithoutADomainTheCheckSaysWhatToSet()
    {
        HttpResponseMessage response = await (await application.AdministratorAsync()).PostAsync("/api/deployments/domain-check", new DomainJoinCheckRequest(null));
        DomainJoinCheckView result = await RegisteredMachine.ReadAsync<DomainJoinCheckView>(response);

        Assert.False(result.CanJoin);
        Assert.Equal("No domain is set. Set the domain and the join account on the Deployment defaults page.", Assert.Single(result.Findings).Text);
    }
}
