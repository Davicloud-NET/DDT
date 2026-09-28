// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ConditionNode } from "@/sequences/sequenceConditions";
import { editedValues, namedValues, type EditedValue } from "@/values/values";

import type { RuleView, SaveRuleRequest } from "./rules";

// What a rule's drawer edits, as typed.
export interface RuleEdit {
  name: string;
  description: string;
  enabled: boolean;
  when: ConditionNode | null;
  sequenceId: string | null;
  values: EditedValue[];
  roleIds: string[];
}

export function editOf(rule: RuleView): RuleEdit {
  return {
    name: rule.name,
    description: rule.description ?? "",
    enabled: rule.enabled,
    when: rule.when,
    sequenceId: rule.sequenceId,
    values: editedValues(rule.values),
    roleIds: rule.roleIds,
  };
}

export function emptyEdit(): RuleEdit {
  return {
    name: "",
    description: "",
    enabled: true,
    when: null,
    sequenceId: null,
    values: [],
    roleIds: [],
  };
}

export function requestOf(revision: number, edit: RuleEdit): SaveRuleRequest {
  const description = edit.description.trim();

  return {
    revision,
    name: edit.name.trim(),
    description: description === "" ? null : description,
    enabled: edit.enabled,
    when: edit.when,
    sequenceId: edit.sequenceId,
    values: namedValues(edit.values),
    roleIds: edit.roleIds,
  };
}
