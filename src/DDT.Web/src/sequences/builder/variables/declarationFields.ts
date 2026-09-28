// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { InputPatch } from "../../flow/flowEdits";
import type { Findings } from "../../problems";
import type { InputDeclaration } from "../../sequences";

export const iconKey =
  "flex size-7.5 shrink-0 cursor-pointer items-center justify-center rounded-key text-ink-2 key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus " +
  "disabled:cursor-not-allowed disabled:opacity-40";

// Empty text means no value.
export function orNull(text: string): string | null {
  return text.trim() === "" ? null : text;
}

// The props each part of an input's fields gets. chosen means a switch or a list made the change, so it's saved at
// once.
export interface InputPartProps {
  input: InputDeclaration;
  // A member's field name, as a finding names it, such as inputs[0].label.
  at: (member: string) => string;
  findings: Findings;
  update: (patch: InputPatch, chosen?: boolean) => void;
}
