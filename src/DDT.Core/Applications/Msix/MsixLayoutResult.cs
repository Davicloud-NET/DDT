// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Applications.Msix;

// Either the layout or the sentence that says what is wrong with the files.
public sealed record MsixLayoutResult(MsixLayout? Layout, ServerMessage? Problem)
{
    // Whether the files hold any package at all, so a zip without one is looked at as another installer.
    public bool HasPackages { get; init; } = true;

    public static MsixLayoutResult Of(MsixLayout layout) => new(layout, null);

    public static MsixLayoutResult Refused(ServerMessage problem) => new(null, problem);
}
