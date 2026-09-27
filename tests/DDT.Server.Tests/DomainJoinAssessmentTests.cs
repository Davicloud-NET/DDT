// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DomainJoinAssessmentTests
{
    private const string Domain = "corp.example";
    private const string User = @"CORP\ddt-join";
    private const string Controller = "dc1.corp.example";
    private const string Computers = "CN=Computers,DC=corp,DC=example";
    private const string Workstations = "OU=Workstations,DC=corp,DC=example";

    private static DomainDirectoryFacts Facts(string? container, bool canCreate = false, int? quota = 10, int created = 0) =>
        new("LDAPS", "DC=corp,DC=example", container, canCreate, quota, created);

    private static DomainJoinAssessment.Verdict Assess(string? organizationalUnit, DomainDirectoryFacts facts) =>
        DomainJoinAssessment.Assess(Domain, User, Controller, organizationalUnit, facts);

    [Fact]
    public void TheRightToCreateComputersInTheOrganizationalUnitIsEnough()
    {
        DomainJoinAssessment.Verdict verdict = Assess(Workstations, Facts(Workstations, canCreate: true));

        Assert.True(verdict.CanJoin);
        Assert.Equal(Workstations, verdict.Container);
        Assert.All(verdict.Findings, finding => Assert.Equal(DomainJoinFindingLevel.Passed, finding.Level));
        Assert.Equal(
            [
                $"Signed in to {Controller} as {User} over LDAPS.",
                $"{Controller} is a domain controller of {Domain}.",
                $"{User} may create computer objects in {Workstations}, as many as it needs.",
            ],
            verdict.Findings.Select(finding => finding.Text));
    }

    [Fact]
    public void WithoutTheRightTheQuotaReachesNoOrganizationalUnit()
    {
        DomainJoinAssessment.Verdict verdict = Assess(Workstations, Facts(Workstations, quota: 10));

        Assert.False(verdict.CanJoin);
        DomainJoinFinding last = verdict.Findings[^1];
        Assert.Equal(DomainJoinFindingLevel.Problem, last.Level);
        Assert.Contains("may not create computer objects in OU=Workstations", last.Text, StringComparison.Ordinal);
        Assert.Contains("Delegation of Control", last.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheRightTheQuotaJoinsIntoTheComputersContainerWithAWarning()
    {
        DomainJoinAssessment.Verdict verdict = Assess(null, Facts(Computers, quota: 10, created: 3));

        Assert.True(verdict.CanJoin);
        DomainJoinFinding last = verdict.Findings[^1];
        Assert.Equal(DomainJoinFindingLevel.Warning, last.Level);
        Assert.Contains("3 of 10 used, 7 left", last.Text, StringComparison.Ordinal);
        Assert.Contains("Add workstations to domain", last.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 12)]
    [InlineData(0, 0)]
    public void AUsedUpQuotaCannotJoin(int quota, int created)
    {
        DomainJoinAssessment.Verdict verdict = Assess(null, Facts(Computers, quota: quota, created: created));

        Assert.False(verdict.CanJoin);
        Assert.Equal(DomainJoinFindingLevel.Problem, verdict.Findings[^1].Level);
        Assert.Contains($"{created} of {quota}", verdict.Findings[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableQuotaCannotBeCountedOn()
    {
        DomainJoinAssessment.Verdict verdict = Assess(null, Facts(Computers, quota: null));

        Assert.False(verdict.CanJoin);
        Assert.Equal(DomainJoinFindingLevel.Warning, verdict.Findings[^1].Level);
    }

    [Fact]
    public void AMissingOrganizationalUnitSaysWhereToCorrectIt()
    {
        DomainJoinAssessment.Verdict verdict = Assess(Workstations, Facts(null, canCreate: true));

        Assert.False(verdict.CanJoin);
        Assert.Null(verdict.Container);
        Assert.Contains("has no organizational unit OU=Workstations", verdict.Findings[^1].Text, StringComparison.Ordinal);
        Assert.Contains("or the one on the Deployment defaults page", verdict.Findings[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllerOfAnotherDomainStopsTheCheck()
    {
        DomainJoinAssessment.Verdict verdict = Assess(null, Facts(Computers, canCreate: true) with { NamingContext = "DC=lab,DC=example" });

        Assert.False(verdict.CanJoin);
        Assert.Equal(2, verdict.Findings.Count);
        Assert.Contains("serves the domain DC=lab,DC=example, not corp.example (DC=corp,DC=example)", verdict.Findings[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDomainsNamingContextIgnoresCaseAndATrailingDot()
    {
        Assert.Equal("DC=corp,DC=example", DomainJoinAssessment.NamingContextOf("corp.example."));
        Assert.True(Assess(null, Facts(Computers, canCreate: true) with { NamingContext = "dc=CORP,dc=example" }).CanJoin);
    }

    [Theory]
    [InlineData("52e", "did not accept the password of CORP\\ddt-join. Correct the join account password on the Deployment defaults page.")]
    [InlineData("525", "knows no account CORP\\ddt-join")]
    [InlineData("532", "has expired. Give it a new one")]
    [InlineData("533", "is disabled")]
    [InlineData("775", "is locked out")]
    [InlineData("773", "has to change its password")]
    [InlineData(null, "did not accept the user name or password of CORP\\ddt-join.")]
    [InlineData("12ab", "(reason 12ab)")]
    public void SaysWhyTheControllerRefusedTheAccount(string? reason, string says)
    {
        Assert.Contains(says, DomainJoinAssessment.DescribeRefusal(reason, Controller, User), StringComparison.Ordinal);
    }
}
