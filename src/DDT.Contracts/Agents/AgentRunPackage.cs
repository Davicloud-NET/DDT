// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// A driver, script or Windows package zip for the step that uses it. A driver step can have several, matched to the
// machine's model.
public sealed record AgentRunPackage(Guid StepId, string Name, string Sha256, long SizeBytes)
{
    // A Windows package the step doesn't list, matched to the image's build. One that doesn't fit is left out.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Matched { get; init; }
}
