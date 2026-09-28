// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "@/sequences/sequenceDraft";
import type {
  GroupStep,
  IfStep,
  RepeatStep,
  RunScriptStep,
  SequenceStep,
} from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";

// Trees for tests, named by their ids: a script leaf, a group, an IF and a repeat.
export function leaf(id: string): RunScriptStep {
  return { ...(newStep("runScript", id) as RunScriptStep), name: `Step ${id}` };
}

export function group(id: string, ...steps: SequenceStep[]): GroupStep {
  return { ...(newStep("group", id) as GroupStep), name: `Group ${id}`, steps };
}

export function branch(id: string, then: SequenceStep[], otherwise: SequenceStep[] = []): IfStep {
  return { ...(newStep("if", id) as IfStep), name: `If ${id}`, then, else: otherwise };
}

export function repeat(id: string, ...steps: SequenceStep[]): RepeatStep {
  return { ...(newStep("repeat", id) as RepeatStep), name: `Repeat ${id}`, steps };
}

export function draftOfSteps(...steps: SequenceStep[]): SequenceDraft {
  return { name: "Lab", description: "", steps, variables: [], inputs: [] };
}
