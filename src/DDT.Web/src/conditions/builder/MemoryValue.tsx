// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useState } from "react";

import { EditorLock } from "@/sequences/editorLock";
import { fieldFindings, type Findings } from "@/sequences/problems";
import { TextField } from "@/ui/TextField";

import { gigabytesOf, megabytesOf } from "../conditionValues";

// Memory is entered in GB. What is typed stays while the field is typed in, so "1." is not turned into "1" before the
// next digit.
export function MemoryValue({
  label,
  field,
  findings,
  value,
  onChange,
}: {
  label: string;
  field: string;
  findings: Findings;
  value: string;
  onChange: (value: string) => void;
}) {
  const locked = useContext(EditorLock);
  const [typed, setTyped] = useState<string | null>(null);
  const { problems } = fieldFindings(findings, field);

  return (
    <div data-field={field} className="flex items-center gap-1.5">
      <TextField
        label={<span className="sr-only">{label}</span>}
        value={typed ?? gigabytesOf(value)}
        isReadOnly={locked}
        mono
        inputMode="decimal"
        autoComplete="off"
        className="min-w-0 flex-1"
        isInvalid={problems.length > 0}
        errorMessage={problems.join(" ")}
        onChange={(text) => {
          setTyped(text);
          onChange(megabytesOf(text));
        }}
        onBlur={() => {
          setTyped(null);
        }}
      />
      <span aria-hidden="true" className="type-small text-muted">
        GB
      </span>
    </div>
  );
}
