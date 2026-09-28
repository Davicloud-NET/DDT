// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

// Field is the field's name on the page, such as bootTargets[X64Uefi].method, and empty for the whole section. Message
// is English; Text is the same sentence as a code with its values, for a client in the person's language. Code names a
// warning a save has to confirm, such as network.wide, and is null for a problem.
public sealed record SettingMessage(string Field, string Message, string? Code, ServerMessage? Text = null);
