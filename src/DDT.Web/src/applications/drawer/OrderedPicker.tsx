// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, type ReactNode } from "react";

import { fieldFindings, type Findings } from "@/sequences/problems";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { ListBoxItem, Select } from "@/ui/Select";

import { PickedRow } from "./PickedRow";

interface OrderedPickerProps {
  label: ReactNode;
  // The field the server's problems name, such as dependsOn; a row's is dependsOn[0].
  field: string;
  ids: readonly string[];
  // Every application that may be picked, with its name.
  options: readonly { id: string; name: string }[];
  addLabel: string;
  findings: Findings;
  onChange: (ids: string[]) => void;
  // Shows the list without the keys that change it.
  isReadOnly?: boolean;
}

// An ordered list of applications: picked from the library, moved up and down, and removed.
export function OrderedPicker(props: OrderedPickerProps) {
  const { label, field, ids, options, addLabel, findings, onChange, isReadOnly = false } = props;
  const labelId = useId();
  const left = options.filter((option) => !ids.includes(option.id));
  const nameOf = (id: string) => options.find((option) => option.id === id)?.name ?? null;

  const move = (from: number, to: number) => {
    const next = [...ids];
    const [moved] = next.splice(from, 1);

    if (moved !== undefined) {
      next.splice(to, 0, moved);
      onChange(next);
    }
  };

  return (
    <div role="group" aria-labelledby={labelId} data-field={field} className="flex flex-col gap-2">
      <span id={labelId} className="type-label text-ink">
        {label}
      </span>
      {ids.length > 0 ? (
        <ol className="flex flex-col gap-1">
          {ids.map((id, index) => (
            <PickedRow
              key={id}
              index={index}
              name={nameOf(id)}
              isLast={index === ids.length - 1}
              isReadOnly={isReadOnly}
              errors={fieldFindings(findings, `${field}[${String(index)}]`).problems}
              onMove={(to) => {
                move(index, to);
              }}
              onRemove={() => {
                onChange(ids.filter((other) => other !== id));
              }}
            />
          ))}
        </ol>
      ) : null}
      <FieldErrorText errors={fieldFindings(findings, field).problems} />
      {left.length > 0 && !isReadOnly ? (
        <Select
          label={<span className="sr-only">{addLabel}</span>}
          placeholder={addLabel}
          value={null}
          onChange={(key) => {
            if (key !== null) {
              onChange([...ids, String(key)]);
            }
          }}
        >
          {left.map((option) => (
            <ListBoxItem key={option.id} id={option.id}>
              {option.name}
            </ListBoxItem>
          ))}
        </Select>
      ) : null}
    </div>
  );
}
