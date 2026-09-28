// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// The account a step uses. It's either a stored account or an Account input the sequence declares, never both. An
// input's answer is only kept for that one run. This never holds a password. DDT hands the password to the step
// itself, never to a script.
public sealed record AccountReference(Guid? AccountId, string? Input);
