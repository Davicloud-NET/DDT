// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isSequenceFilter, type SequenceFilter } from "./sequenceList";

// The search parameters of the sequence list: which sequences show, and the search text. They live in the address,
// so going back from a sequence finds the list as it was.
export interface SequencesSearch {
  state?: SequenceFilter;
  q?: string;
}

export function sequencesSearch(search: Record<string, unknown>): SequencesSearch {
  return {
    ...(isSequenceFilter(search.state) && search.state !== "all" ? { state: search.state } : {}),
    ...(typeof search.q === "string" && search.q !== "" ? { q: search.q } : {}),
  };
}

// The search parameters of a sequence's editor: the step it shows, by id, so a reload shows the same step.
export interface SequenceSearch {
  step?: string;
}

export function sequenceSearch(search: Record<string, unknown>): SequenceSearch {
  return typeof search.step === "string" && search.step !== "" ? { step: search.step } : {};
}
