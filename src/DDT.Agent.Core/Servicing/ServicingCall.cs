// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

// One DISM call: the files it adds and the packages they come from. An update's call names only its newest .msu,
// and DISM finds the checkpoints beside it.
public sealed record ServicingCall(bool IsUpdate, IReadOnlyList<string> Files, IReadOnlyList<UnpackedPackage> Packages)
{
    public string Names => string.Join(", ", Packages.Select(package => package.Package.Name).Distinct(StringComparer.Ordinal));
}
