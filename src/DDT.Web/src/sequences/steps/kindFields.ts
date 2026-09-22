// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Findings } from "../problems";
import type { StepPatch } from "../sequenceEdits";
import type { SequenceStep } from "../sequences";
import type { StepCatalog } from "../useSequenceEditor";

// What every kind's fields get: the step, the server's findings for it, the lists to choose from, and the
// change to make.
export interface KindFieldsProps<S extends SequenceStep> {
  step: S;
  findings: Findings;
  catalog: StepCatalog;
  onChange: (patch: StepPatch) => void;
}

// A text setting where nothing entered takes the server's default.
export function orNull(text: string): string | null {
  return text.trim() === "" ? null : text;
}
