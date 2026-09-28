// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import type { ResolvedValue } from "@/values/values";

import {
  answersOf,
  initialDrafts,
  missingAnswers,
  type AnswerDraft,
  type AnswerDrafts,
  type AskedInput,
  type InputAnswer,
} from "./inputs";

// A form's answers while they're edited. Each field starts from the default the server worked out for the machine. It
// also tracks what blocks sending them.
export function useAnswers(inputs: readonly AskedInput[], defaults: readonly ResolvedValue[] = []) {
  const [edits, setEdits] = useState<AnswerDrafts>({});
  const [missing, setMissing] = useState<Record<string, string>>({});
  const drafts = { ...initialDrafts(inputs, defaults), ...edits };

  return {
    drafts,
    missing,
    edit: (name: string, draft: AnswerDraft) => {
      setEdits((previous) => ({ ...previous, [name]: draft }));
      setMissing((previous) =>
        Object.fromEntries(Object.entries(previous).filter(([key]) => key !== name)),
      );
    },
    // The answers to send, or null if a required one is missing. The field then says so.
    collect: (): InputAnswer[] | null => {
      const problems = missingAnswers(inputs, drafts);

      setMissing(problems);

      return Object.keys(problems).length > 0 ? null : answersOf(inputs, drafts);
    },
    // Forgets the typed passwords, for example after the answers were sent or the form closed.
    forgetPasswords: () => {
      setEdits((previous) =>
        Object.fromEntries(
          Object.entries(previous).map(([name, draft]) => [name, { ...draft, password: "" }]),
        ),
      );
    },
  };
}

export type Answers = ReturnType<typeof useAnswers>;
