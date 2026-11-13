// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Applications;

namespace DDT.Contracts.Sequences;

// Installs applications and bundles from the library, in order, each after its dependencies. Version 6.
public sealed record InstallApplicationsStep : SequenceStep
{
    public IReadOnlyList<ApplicationReference> Applications { get; init; } = [];

    // A value whose `;` list names more applications or bundles, by id or name, installed after the list.
    public string? Variable { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => 6;

    // An installer that was interrupted runs again, unless its detection rule finds it installed.
    [JsonIgnore]
    public override bool Resumable => true;

    [JsonIgnore]
    public override int RestartLimit => 20;
}
