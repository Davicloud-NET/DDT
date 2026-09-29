// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Facts;

namespace DDT.Agent.Tests;

// Memory and processors as each test sets them. Windows reports nothing unless the test says so. A function that
// throws stands for a call that fails in a way Windows doesn't report.
internal sealed class FakeSystemHardware : ISystemHardware
{
    public Func<ulong?> Installed { get; set; } = () => null;

    public Func<ulong?> Usable { get; set; } = () => null;

    public Func<byte[]?> Cores { get; set; } = () => null;

    public ulong? InstalledMemoryKilobytes() => Installed();

    public ulong? UsableMemoryBytes() => Usable();

    public byte[]? ProcessorCores() => Cores();
}
