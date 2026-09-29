// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Findings } from "../problems";
import type { StepPatch } from "../sequenceEdits";
import type { SequenceStep } from "../sequences";
import type { StepCatalog } from "../useStepCatalog";

// The props every kind's fields get: the step, the server's findings for it, the lists to choose from, and the
// change to make. chosen means a switch made the change, so it's saved at once, even if it sets a text field.
export interface KindFieldsProps<S extends SequenceStep> {
  step: S;
  findings: Findings;
  catalog: StepCatalog;
  onChange: (patch: StepPatch, chosen?: boolean) => void;
}

// A text setting that takes the server's default when it's left empty.
export function orNull(text: string): string | null {
  return text.trim() === "" ? null : text;
}
