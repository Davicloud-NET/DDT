// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// What a boot environment's agent found on a disk of a run that was handed over to the installed system, without the
// run's token. RunId is the run the state there names. Null when the state names none that can be read. Start is a
// random id the agent makes once per start, so the server counts a question it has to ask again only once.
public sealed record AgentHandedOverRun(
    Guid? RunId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Start = null);
