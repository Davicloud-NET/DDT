// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What a run's answer file is made from besides its step. ProductKey is the Deployment defaults' secret, read when the
// agent fetches the file, like Password. MacAddress is the machine's primary one, which a static network is set on.
public sealed record UnattendSources(RunInputs Inputs)
{
    public string? ImageLanguage { get; init; }

    public string? Password { get; init; }

    public string? ProductKey { get; init; }

    public string? MacAddress { get; init; }

    // A record prints every property by default, and the secrets must never reach a log.
    public override string ToString() => $"UnattendSources {{ ImageLanguage = {ImageLanguage} }}";
}
