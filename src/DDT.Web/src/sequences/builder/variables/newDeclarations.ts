// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../../sequenceDraft";
import type { InputDeclaration, VariableDeclaration } from "../../sequences";

// Variables and inputs share one set of names, compared ignoring case.
function freeName(draft: SequenceDraft, stem: string): string {
  const taken = [...draft.variables, ...draft.inputs].map((item) => item.name.toLowerCase());
  let number = 1;

  while (taken.includes(`${stem}${String(number)}`.toLowerCase())) {
    number++;
  }

  return `${stem}${String(number)}`;
}

export function newVariable(draft: SequenceDraft): VariableDeclaration {
  const name = freeName(draft, "Variable");

  return { name, default: null, description: null, setBySteps: false };
}

export function newInput(draft: SequenceDraft): InputDeclaration {
  const name = freeName(draft, "Input");

  return {
    name,
    label: name,
    help: null,
    kind: "Text",
    choices: [],
    default: null,
    required: false,
    maxLength: null,
    askAt: "Web",
    account: null,
  };
}
