// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconChevronDown } from "@tabler/icons-react";
import { Button as AriaButton, Popover, Select, SelectValue } from "react-aria-components";

import { fieldFindings, type Findings } from "@/sequences/problems";
import { cx } from "@/ui/cx";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { TextField, fieldClass } from "@/ui/TextField";

import type { Subject } from "../conditionSubjects";
import { SubjectList } from "./SubjectList";

// Chooses what a test tests, from the sections of subjects.
export function SubjectPicker({
  label,
  field,
  findings,
  subject,
  subjects,
  locked,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  subject: Subject;
  subjects: readonly Subject[];
  locked: boolean;
  onChange: (name: string) => void;
}) {
  const { problems } = fieldFindings(findings, field);

  if (locked) {
    return (
      <div data-field={field}>
        <TextField
          label={<span className="sr-only">{label}</span>}
          value={subject.label}
          isReadOnly
        />
      </div>
    );
  }

  // The fields beside it keep a label for screen readers, and the gap under that label pushes them down. The padding
  // lines this one up with them.
  return (
    <div data-field={field} className="pt-1.5">
      <Select
        aria-label={label}
        value={subject.name}
        isInvalid={problems.length > 0}
        onChange={(key) => {
          if (key !== null && String(key) !== subject.name) {
            onChange(String(key));
          }
        }}
      >
        <AriaButton
          className={cx(
            fieldClass,
            "flex h-9.5 cursor-pointer items-center gap-1.5 pr-1.5 text-left type-body",
            problems.length > 0 && "shadow-[inset_0_0_0_1.5px_var(--color-fail-text)]",
          )}
        >
          <SelectValue className="flex-1 truncate">{() => subject.label}</SelectValue>
          <IconChevronDown
            aria-hidden="true"
            size={14}
            stroke={2}
            className="shrink-0 text-muted"
          />
        </AriaButton>
        <Popover
          offset={4}
          placement="bottom start"
          className="flex w-72 max-w-[calc(100vw-2rem)] flex-col rounded-overlay bg-raised p-2 shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out"
        >
          <SubjectList label={label} subject={subject} subjects={subjects} />
        </Popover>
      </Select>
      <FieldErrorText errors={problems} />
    </div>
  );
}
