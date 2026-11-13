// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AnswerDraft, AskedInput } from "@/inputs/inputs";

// The props every input field gets, whatever its kind.
export interface InputFieldProps {
  input: AskedInput;
  draft: AnswerDraft;
  // The input's help, and where its first value came from.
  hint: string | null;
  // Why the page or the server refused the answer.
  error: string | null;
  onChange: (draft: AnswerDraft) => void;
}

// A choice's answers as its buttons and its list show them.
export function choicesOf(input: AskedInput): { value: string; label: string }[] {
  return input.choices.map((choice) => ({
    value: choice.value,
    label: choice.label ?? choice.value,
  }));
}

// A list of more choices than this gets a search field.
export const SEARCH_ABOVE = 8;

// The choices whose label holds the search, ignoring case.
export function matchingChoices<T extends { label: string }>(
  choices: readonly T[],
  search: string,
): T[] {
  const wanted = search.trim().toLocaleLowerCase();

  return choices.filter((choice) => choice.label.toLocaleLowerCase().includes(wanted));
}
