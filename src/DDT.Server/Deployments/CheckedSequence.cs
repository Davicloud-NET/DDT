// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// A sequence as a new run would take it, with the library and the settings of the moment. Problem keeps it from running.
public sealed record CheckedSequence(TaskSequence Sequence, SequenceDefinition Definition, SequenceReferences References, ServerMessage? Problem);
