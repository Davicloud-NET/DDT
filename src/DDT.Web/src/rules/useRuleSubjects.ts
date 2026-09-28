// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo } from "react";

import type { Subject } from "@/conditions/conditionSubjects";
import { useConditionData } from "@/conditions/subjects";

import { noDeclarations } from "./ruleData";

// What a rule's condition can test: the machine's facts and the values that rules and machine roles set. Values a run
// sets, such as whether its last step failed, don't exist yet when the rules are checked, so the server refuses them.
export function useRuleSubjects(): Subject[] {
  const { subjects } = useConditionData(noDeclarations);

  return useMemo(() => subjects.filter((subject) => subject.section !== "run"), [subjects]);
}
