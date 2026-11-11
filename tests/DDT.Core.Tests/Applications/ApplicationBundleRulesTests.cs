// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

public sealed class ApplicationBundleRulesTests
{
    private static readonly Guid s_first = Guid.Parse("0197a3c0-0000-7000-8000-000000000001");
    private static readonly Guid s_second = Guid.Parse("0197a3c0-0000-7000-8000-000000000002");

    private static readonly SaveApplicationBundleRequest s_bundle = new(0, "Office workstation", "What every office PC gets.", [s_first, s_second]);

    [Fact]
    public void AcceptsABundle() => Assert.Empty(ApplicationRules.Check(s_bundle));

    [Fact]
    public void NeedsAtLeastOneMember() =>
        AssertProblems(s_bundle with { Members = [] }, ("members", ServerMessages.ApplicationBundleMembers));

    [Fact]
    public void RefusesMoreMembersThanTheLimit() =>
        AssertProblems(
            s_bundle with { Members = [.. Enumerable.Range(0, ApplicationLimits.MaxBundleMembers + 1).Select(_ => Guid.NewGuid())] },
            ("members", ServerMessages.ApplicationBundleMembers));

    [Fact]
    public void RefusesAMemberListedTwiceOrEmpty() =>
        AssertProblems(
            s_bundle with { Members = [s_first, Guid.Empty, s_first] },
            ("members[1]", ServerMessages.ApplicationGone),
            ("members[2]", ServerMessages.ApplicationListedTwice));

    [Fact]
    public void ChecksTheNameAndDescriptionLikeAnApplications() =>
        AssertProblems(
            s_bundle with { Name = "Office;Lab", Description = new string('d', ApplicationLimits.MaxDescriptionLength + 1) },
            ("name", ServerMessages.ApplicationNameSemicolon),
            ("description", ServerMessages.DescriptionLength));

    private static void AssertProblems(SaveApplicationBundleRequest request, params (string Field, MessageTemplate Message)[] expected) =>
        Assert.Equal(
            expected.Select(problem => (problem.Field, problem.Message.Code)),
            ApplicationRules.Check(request).Select(problem => (problem.Field, problem.Message.Code)));
}
