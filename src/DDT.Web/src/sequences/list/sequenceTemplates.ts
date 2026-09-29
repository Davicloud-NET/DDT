// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { serverText } from "@/lib/serverText";

import type { SequenceTemplate } from "../sequences";

// The key for an empty sequence. Template keys are words such as "install-windows".
export const EMPTY_CHOICE = "empty";

// The server's templates, with their name and description in the person's language. A new sequence takes both.
export function templatesInLanguage(templates: readonly SequenceTemplate[]): SequenceTemplate[] {
  return templates.map((candidate) => ({
    ...candidate,
    name: serverText(candidate.nameCode, candidate.nameArgs, candidate.name),
    description: serverText(
      candidate.descriptionCode,
      candidate.descriptionArgs,
      candidate.description,
    ),
  }));
}
