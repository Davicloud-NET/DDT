// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;

namespace DDT.Server.Applications;

// Where a sequence names an application or a bundle: in a step's list, or in what an input offers. Field is the
// reference's path, such as applications[2] in the step or inputs[0].offered[1] in the sequence.
public sealed record ApplicationSite(ApplicationReference Reference, SequenceStep? Step, InputDeclaration? Input, string Field);
