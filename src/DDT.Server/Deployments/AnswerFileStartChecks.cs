// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// The answer file's product key and static network a run starts with. A wrong one ends the run before any disk is
// touched, instead of at the step after the image is applied.
public static class AnswerFileStartChecks
{
    public static string? Problem(SequenceDefinition definition, IReadOnlyDictionary<string, string> values, DeploymentOptions deployment, string macAddress)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(deployment);

        if (!SequenceTree.Nodes(definition).Any(node => node is WriteUnattendStep))
        {
            return null;
        }

        string? Value(string name) => values.GetValueOrDefault(name);

        string? problem = AnswerFileValues.ProductKeyOf(Value, deployment.ProductKey).Problem
            ?? AnswerFileValues.NetworkOf(Value, macAddress).Problem;

        return problem is null ? null : $"{problem} So the run did not start.";
    }
}
