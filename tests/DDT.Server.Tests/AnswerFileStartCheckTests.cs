// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AnswerFileStartCheckTests
{
    private static readonly SequenceDefinition s_sequence = new(3, [new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file" }]);

    private static string? Problem(DeploymentOptions options, params (string Name, string Value)[] values) =>
        AnswerFileStartChecks.Problem(
            s_sequence,
            values.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase),
            options,
            "00155D010203");

    [Fact]
    public void AcceptsGoodValues()
    {
        Assert.Null(Problem(new DeploymentOptions { ProductKey = "NPPR9-FWDCX-D2C8J-H872K-2YT43" }, ("IPAddress", "10.0.4.51/24"), ("Gateway", "10.0.4.1")));
    }

    [Fact]
    public void RefusesAWrongKeyWithoutNamingIt()
    {
        string? problem = Problem(new DeploymentOptions(), ("ProductKey", "SECRET-12345"));

        Assert.Equal("The run's ProductKey value is not five groups of five letters or digits. So the run did not start.", problem);
        Assert.DoesNotContain("SECRET", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesANetworkValueByItsName()
    {
        Assert.StartsWith("The run's Gateway value, 10.0.4, ", Problem(new DeploymentOptions(), ("IPAddress", "10.0.4.51/24"), ("Gateway", "10.0.4")), StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesSequencesWithoutAnAnswerFileAlone()
    {
        Assert.Null(AnswerFileStartChecks.Problem(
            new SequenceDefinition(7, []),
            new Dictionary<string, string> { ["ProductKey"] = "wrong" },
            new DeploymentOptions(),
            "00155D010203"));
    }
}
