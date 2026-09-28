// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo } from "react";

import type { Subject } from "@/conditions/conditions";
import { useConditionData } from "@/conditions/subjects";
import type { InputDeclaration, VariableDeclaration } from "@/sequences/sequences";

// Rules and machine roles belong to no sequence: they declare no variables and ask nothing.
export const noDeclarations: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
} = { variables: [], inputs: [] };

// What a rule's condition can test: the machine's facts and the values rules and machine roles set. What a run sets,
// such as whether its last step failed, has no value when the rules are checked, so the server refuses it.
export function useRuleSubjects(): Subject[] {
  const { subjects } = useConditionData(noDeclarations);

  return useMemo(() => subjects.filter((subject) => subject.section !== "run"), [subjects]);
}
