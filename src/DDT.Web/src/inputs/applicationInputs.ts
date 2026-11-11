// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { entryOf, type LibraryEntry } from "@/applications/libraryEntries";
import type { InputChoice, InputDeclaration } from "@/sequences/sequences";

import type { AskedInput } from "./inputs";

// The server's ApplicationInputs: a MultiChoice of the offered entries still in the library, by id with their current
// names, and the ticked ones still offered as its default.
export function askedApplications(
  input: InputDeclaration,
  library: readonly LibraryEntry[],
): AskedInput {
  const choices: InputChoice[] = [];

  for (const reference of input.offered ?? []) {
    const entry = entryOf(reference, library);

    if (entry !== null && !choices.some((choice) => choice.value === entry.id)) {
      choices.push({ value: entry.id, label: entry.name });
    }
  }

  const ticked = [
    ...new Set((input.ticked ?? []).filter((id) => choices.some((choice) => choice.value === id))),
  ];

  return {
    name: input.name,
    label: input.label,
    help: input.help,
    kind: "MultiChoice",
    choices,
    default: ticked.length > 0 ? ticked.join(";") : null,
    required: input.required,
    maxLength: null,
    account: null,
  };
}
