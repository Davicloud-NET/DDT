// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext } from "react";

import { EditorLock } from "@/sequences/editorLock";
import {
  conditionPath,
  type ConditionChange,
  type ConditionField,
} from "@/sequences/flow/conditionTree";
import type { Findings } from "@/sequences/problems";
import type { ConditionNode } from "@/sequences/sequenceConditions";

import type { ConditionUse } from "../conditions";
import type { Subject } from "../conditionSubjects";
import type { RowContext } from "./rowContext";

interface ConditionRowsOptions {
  use: ConditionUse;
  value: ConditionNode | null;
  subjects: readonly Subject[];
  field: ConditionField;
  fieldOf: ((path: readonly number[]) => string) | undefined;
  findings: Findings;
  onChange: (path: readonly number[], change: ConditionChange) => void;
  isReadOnly: boolean;
}

function numbering(value: ConditionNode | null): Map<string, number> {
  const numbers = new Map<string, number>();

  const visit = (node: ConditionNode, path: number[]) => {
    if (node.kind === "test") {
      numbers.set(path.join("."), numbers.size + 1);
    } else {
      node.parts.forEach((part, index) => {
        visit(part, [...path, index]);
      });
    }
  };

  if (value !== null) {
    visit(value, []);
  }

  return numbers;
}

export function useConditionRows({
  use,
  value,
  subjects,
  field,
  fieldOf,
  findings,
  onChange,
  isReadOnly,
}: ConditionRowsOptions): RowContext {
  const locked = useContext(EditorLock) || isReadOnly;
  const place = fieldOf ?? ((path: readonly number[]) => conditionPath(field, path));
  // A when or a rule can be empty, and then it always holds. An IF's test and a Repeat's until keep an empty group.
  const mayBeNone = use === "when" || use === "rule";
  const numbers = numbering(value);

  const remove = (path: readonly number[]) => {
    const [only] = path;

    if (
      mayBeNone &&
      value !== null &&
      (path.length === 0 ||
        (path.length === 1 && only === 0 && value.kind !== "test" && value.parts.length === 1))
    ) {
      onChange([], { op: "set", node: null });
    } else {
      onChange(path, { op: "remove" });
    }
  };

  return { subjects, place, findings, onChange, remove, locked, numbers };
}
