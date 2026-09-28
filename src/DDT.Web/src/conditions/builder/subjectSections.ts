// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Subject, SubjectSection } from "../conditionSubjects";

// The picker's sections in order, the empty ones left out. A subject no list has, such as a variable removed since,
// is still offered, so the picker can show what the test names.
export function subjectSections(
  subject: Subject,
  subjects: readonly Subject[],
): { section: SubjectSection; items: Subject[] }[] {
  const listed = subjects.some((candidate) => candidate.name === subject.name);
  const all = listed ? subjects : [...subjects, subject];

  return (["machine", "run", "values", "sequence"] as const)
    .map((section) => ({ section, items: all.filter((item) => item.section === section) }))
    .filter((section) => section.items.length > 0);
}
