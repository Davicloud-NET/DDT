// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// The Linux boot environment's folder, the names in it that may be served with what each was when it was checked, and
// what the generated files carry: the server's address and root for the agent, the keymap and the administrator's
// kernel arguments. None is a secret.
public sealed record LinuxBootBundle(string Folder, IReadOnlyDictionary<string, BootFileStamp> Files, Uri ServerUrl, string RootCertificatePem)
{
    public string Keymap { get; init; } = GrubConfig.DefaultKeymap;

    // The layout's name, which the agent shows at the sign-in.
    public string? KeyboardLayout { get; init; }

    public string KernelArguments { get; init; } = string.Empty;

    // Told the name of a listed file that is gone, or no longer what it was when it was checked.
    public Action<string>? Changed { get; init; }
}
