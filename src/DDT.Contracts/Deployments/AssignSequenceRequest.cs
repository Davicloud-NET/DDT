// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Deployments;

// AllowSecureBootMismatch lets a raw disk image that is not signed for Secure Boot be written although the machine has
// Secure Boot on. Answers are the answers to the sequence's inputs asked on the web.
public sealed record AssignSequenceRequest(
    Guid SequenceId,
    string? ComputerName,
    bool AllowSecureBootMismatch = false,
    IReadOnlyList<InputAnswer>? Answers = null);
