// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// The account a step uses: exactly one of a stored account and an Account input the sequence declares, whose answer is
// kept for the one run. Never a password: DDT hands it to the step itself, never to a script.
public sealed record AccountReference(Guid? AccountId, string? Input);
