// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;

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

        List<DomainJoinFinding> findings = [Passed(ServerMessages.DomainSignedIn.With("controller", controller, "user", userName, "connection", facts.Connection))];
        string expected = NamingContextOf(domain);

        if (!string.Equals(facts.NamingContext, expected, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Problem(ServerMessages.DomainOtherDomain.With(
                "controller",
                controller,
                "namingContext",
                facts.NamingContext,
                "domain",
                domain,
                "expected",
                expected)));

            return new(false, null, findings);
        }

        findings.Add(Passed(ServerMessages.DomainControllerOf.With("controller", controller, "domain", domain)));

        if (facts.Container is not { } container)
        {
            findings.Add(Problem(organizationalUnit is null
                ? ServerMessages.DomainNoComputersContainer.With("domain", domain, "user", userName)
                : ServerMessages.DomainNoOrganizationalUnit.With("domain", domain, "organizationalUnit", organizationalUnit, "user", userName)));

            return new(false, null, findings);
        }

        if (facts.CanCreateComputers)
        {
            findings.Add(Passed(ServerMessages.DomainMayCreate.With("user", userName, "container", container)));

            return new(true, container, findings);
        }

        if (organizationalUnit is not null)
        {
            findings.Add(Problem(ServerMessages.DomainMayNotCreateInUnit.With("user", userName, "container", container)));

            return new(false, container, findings);
        }

        if (facts.MachineAccountQuota is not { } quota)
        {
            findings.Add(Warning(ServerMessages.DomainQuotaUnknown.With("user", userName, "container", container)));

            return new(false, container, findings);
        }

        int left = Math.Max(0, quota - facts.ComputersCreated);

        if (left == 0)
        {
            findings.Add(Problem(ServerMessages.DomainQuotaUsed.With(
                "user",
                userName,
                "container",
                container,
                "created",
                facts.ComputersCreated,
                "quota",
                quota)));

            return new(false, container, findings);
        }

        findings.Add(Warning(ServerMessages.DomainQuotaLeft.With(
            "user",
            userName,
            "container",
            container,
            "created",
            facts.ComputersCreated,
            "quota",
            quota,
            "left",
            left)));

        return new(true, container, findings);
    }

    // Active Directory's reason codes for a refused LDAP sign-in.
    public static string DescribeRefusal(string? reasonCode, string controller, string userName) => RefusalMessage(reasonCode, controller, userName).Text;

    public static ServerMessage RefusalMessage(string? reasonCode, string controller, string userName) => reasonCode switch
    {
        "525" => ServerMessages.DomainNoSuchAccount.With("controller", controller, "user", userName),
        "52e" => ServerMessages.DomainWrongPassword.With("controller", controller, "user", userName),
        "530" => ServerMessages.DomainLogonHours.With("user", userName),
        "531" => ServerMessages.DomainLogonWorkstations.With("user", userName),
        "532" => ServerMessages.DomainPasswordExpired.With("user", userName),
        "533" => ServerMessages.DomainAccountDisabled.With("user", userName),
        "701" => ServerMessages.DomainAccountExpired.With("user", userName),
        "773" => ServerMessages.DomainMustChangePassword.With("user", userName),
        "775" => ServerMessages.DomainLockedOut.With("user", userName),
        null => ServerMessages.DomainSignInRefused.With("controller", controller, "user", userName),
        _ => ServerMessages.DomainSignInRefusedWithReason.With("controller", controller, "user", userName, "reason", reasonCode),
    };

    public static string DescribeUnreachable(string controller, string domain, string? detail) => UnreachableMessage(controller, domain, detail).Text;

    public static ServerMessage UnreachableMessage(string controller, string domain, string? detail) =>
        detail is null
            ? ServerMessages.DomainUnreachable.With("controller", controller, "domain", domain)
            : ServerMessages.DomainUnreachableWithDetail.With("controller", controller, "detail", detail.TrimEnd('.'), "domain", domain);

    public static string DescribeNoSecureConnection(string controller) => NoSecureConnectionMessage(controller).Text;

    public static ServerMessage NoSecureConnectionMessage(string controller) => ServerMessages.DomainNoSecureConnection.With("controller", controller);

    // corp.example becomes DC=corp,DC=example.
    public static string NamingContextOf(string domain) =>
        string.Join(',', domain.Trim().TrimEnd('.').Split('.').Select(label => $"DC={label}"));

    private static DomainJoinFinding Passed(ServerMessage message) => DomainJoinFinding.From(DomainJoinFindingLevel.Passed, message);

    private static DomainJoinFinding Warning(ServerMessage message) => DomainJoinFinding.From(DomainJoinFindingLevel.Warning, message);

    private static DomainJoinFinding Problem(ServerMessage message) => DomainJoinFinding.From(DomainJoinFindingLevel.Problem, message);
}
