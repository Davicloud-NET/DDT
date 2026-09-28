// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext } from "react";

import { EditorLock } from "@/sequences/editorLock";
import { Checkbox } from "@/ui/Checkbox";

import type { Subject } from "../conditionSubjects";
import { listOf } from "../conditionValues";

// Several of a list's choices, as a test with In takes them.
export function ChoiceList({
  label,
  field,
  subject,
  value,
  onChange,
}: {
  label: string;
  field: string;
  subject: Subject;
  value: string;
  onChange: (value: string) => void;
}) {
  const locked = useContext(EditorLock);
  const chosen = listOf(value);

  return (
    <div
      data-field={field}
      role="group"
      aria-label={label}
      className="flex flex-wrap gap-x-3 gap-y-1 py-1.5"
    >
      {subject.choices.map((choice) => (
        <Checkbox
          key={choice.value}
          isSelected={chosen.includes(choice.value)}
          isReadOnly={locked}
          onChange={(selected) => {
            const next = selected
              ? [...chosen, choice.value]
              : chosen.filter((item) => item !== choice.value);

            onChange(
              subject.choices
                .map((item) => item.value)
                .filter((item) => next.includes(item))
                .join(";"),
            );
          }}
        >
          <span className="type-small">{choice.label}</span>
        </Checkbox>
      ))}
    </div>
  );
}
