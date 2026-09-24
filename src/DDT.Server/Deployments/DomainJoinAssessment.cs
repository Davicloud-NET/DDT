// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

// What the directory's answers mean for a Join the domain step, and what to change. NetJoinDomain creates the computer
// account in the organizational unit, or in the default Computers container without one, and the domain controller lets
// it when the account may create computer objects there. Without that right, an account may still add computers to the
// default container while the domain's machine account quota lasts, but never to an organizational unit.
public static class DomainJoinAssessment
{
    public sealed record Verdict(bool CanJoin, string? Container, IReadOnlyList<DomainJoinFinding> Findings);

    public static Verdict Assess(string domain, string userName, string controller, string? organizationalUnit, DomainDirectoryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        List<DomainJoinFinding> findings = [Passed($"Signed in to {controller} as {userName} over {facts.Connection}.")];
        string expected = NamingContextOf(domain);

        if (!string.Equals(facts.NamingContext, expected, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Problem(
                $"{controller} serves the domain {facts.NamingContext}, not {domain} ({expected}). Correct DDT:Deployment:Domain:Name, or " +
                "point DDT:Deployment:Domain:Controller at a domain controller of that domain."));

            return new(false, null, findings);
        }

        findings.Add(Passed($"{controller} is a domain controller of {domain}."));

        if (facts.Container is not { } container)
        {
            findings.Add(Problem(organizationalUnit is null
                ? $"The default Computers container of {domain} was not found, or {userName} may not read it."
                : $"{domain} has no organizational unit {organizationalUnit}, or {userName} may not read it. Correct the organizational " +
                  "unit of the Join the domain step, or DDT:Deployment:Domain:OrganizationalUnit when the step names none."));

            return new(false, null, findings);
        }

        if (facts.CanCreateComputers)
        {
            findings.Add(Passed($"{userName} may create computer objects in {container}, as many as it needs."));

            return new(true, container, findings);
        }

        if (organizationalUnit is not null)
        {
            findings.Add(Problem(
                $"{userName} may not create computer objects in {container}, and the machine account quota does not reach an " +
                "organizational unit. Delegate \"Create Computer objects\" on it to the account, for example with the Delegation of " +
                "Control wizard of Active Directory Users and Computers."));

            return new(false, container, findings);
        }

        if (facts.MachineAccountQuota is not { } quota)
        {
            findings.Add(Warning(
                $"{userName} may not create computer objects in {container} by a right of its own, and the domain's machine account " +
                "quota could not be read. Delegate \"Create Computer objects\" on the container to the account."));

            return new(false, container, findings);
        }

        int left = Math.Max(0, quota - facts.ComputersCreated);

        if (left == 0)
        {
            findings.Add(Problem(
                $"{userName} may not create computer objects in {container} by a right of its own, and has used its machine account " +
                $"quota: {facts.ComputersCreated} of {quota} (ms-DS-MachineAccountQuota). Delegate \"Create Computer objects\" on the " +
                "container to the account."));

            return new(false, container, findings);
        }

        findings.Add(Warning(
            $"{userName} may not create computer objects in {container} by a right of its own, so it joins within the domain's " +
            $"machine account quota: {facts.ComputersCreated} of {quota} used, {left} left. That also needs the user right \"Add " +
            "workstations to domain\", which Authenticated Users hold by default and this check cannot read. Delegate \"Create " +
            "Computer objects\" on the container to the account to join more."));

        return new(true, container, findings);
    }

    // Active Directory's reason codes for a refused LDAP sign-in.
    public static string DescribeRefusal(string? reasonCode, string controller, string userName) => reasonCode switch
    {
        "525" => $"{controller} knows no account {userName}. Correct DDT:Deployment:Domain:UserName.",
        "52e" => $"{controller} did not accept the password of {userName}. Correct DDT:Deployment:Domain:Password.",
        "530" => $"{userName} may not sign in at this time of day (logon hours).",
        "531" => $"{userName} may not sign in from the DDT server (Log On To workstations).",
        "532" => $"The password of {userName} has expired. Give it a new one, in the domain and in DDT:Deployment:Domain:Password.",
        "533" => $"{userName} is disabled.",
        "701" => $"{userName} has expired.",
        "773" => $"{userName} has to change its password before it can sign in. Give it a new one, in the domain and in DDT:Deployment:Domain:Password.",
        "775" => $"{userName} is locked out.",
        _ => $"{controller} did not accept the user name or password of {userName}" + (reasonCode is null ? "." : $" (reason {reasonCode})."),
    };

    public static string DescribeUnreachable(string controller, string domain, string? detail) =>
        $"{controller} could not be reached over LDAP" + (detail is null ? "" : $" ({detail.TrimEnd('.')})") + ". If this server's DNS does not " +
        $"know {domain}, set DDT:Deployment:Domain:Controller to a domain controller's name or address. The machines find their " +
        "domain controller through their own DNS.";

    public static string DescribeNoSecureConnection(string controller) =>
        $"{controller} offers no LDAPS on port 636 that this server trusts, and on this operating system only LDAPS keeps the join " +
        "account's password secret during the check. Give the domain controllers a certificate, for example from Active Directory " +
        "Certificate Services, and trust its CA on this server. Joining does not depend on this check.";

    // corp.example becomes DC=corp,DC=example.
    public static string NamingContextOf(string domain) =>
        string.Join(',', domain.Trim().TrimEnd('.').Split('.').Select(label => $"DC={label}"));

    private static DomainJoinFinding Passed(string text) => new(DomainJoinFindingLevel.Passed, text);

    private static DomainJoinFinding Warning(string text) => new(DomainJoinFindingLevel.Warning, text);

    private static DomainJoinFinding Problem(string text) => new(DomainJoinFindingLevel.Problem, text);
}
