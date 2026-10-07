// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Agent;

// Secure Boot as Windows tells it: the state from the registry, and db through the firmware variables.
public sealed class WindowsSecureBoot : ISecureBootReader
{
    public bool? Enabled() => SecureBootState.Read();

    public UefiCa? TrustedCas() => SecureBootTrust.Read();
}
