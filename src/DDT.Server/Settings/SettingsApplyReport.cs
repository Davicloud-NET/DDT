// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Data;

namespace DDT.Server.Settings;

// How this host applied a version of a section. Message is in English. Text is the same sentence as a message code,
// when it's one DDT knows. Detail is a JSON object, such as the interfaces a PXE host found.
public sealed record SettingsApplyReport(string Section, long Version, SettingsApplyResult Result, string? Message)
{
    public string? Detail { get; init; }

    public ServerMessage? Text { get; init; }
}
