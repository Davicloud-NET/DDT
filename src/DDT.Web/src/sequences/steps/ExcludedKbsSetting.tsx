// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useContext, useState } from "react";

import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { fieldFindings, type Findings } from "../problems";

const field = "excludedKbs";

// "KB5031234, 5031235" as a list. Commas, semicolons and spaces all separate.
function parseKbs(text: string): string[] {
  return text.split(/[\s,;]+/).filter((kb) => kb !== "");
}

// The updates a Windows Update step leaves out. The typed text stays while someone types, and the step keeps its list.
export function ExcludedKbsSetting({
  findings,
  value,
  onChange,
}: {
  findings: Findings;
  value: readonly string[];
  onChange: (value: string[]) => void;
}) {
  const locked = useContext(EditorLock);
  const [text, setText] = useState<string | null>(null);
  const problems = [
    ...fieldFindings(findings, field).problems,
    ...value.flatMap((kb, index) =>
      fieldFindings(findings, `${field}[${String(index)}]`).problems.map(
        (problem) => `${kb}: ${problem}`,
      ),
    ),
  ];

  return (
    <div data-field={field} className="sm:col-span-2">
      <TextField
        label={<Trans>Leave out these updates</Trans>}
        value={text ?? value.join(", ")}
        isReadOnly={locked}
        mono
        autoComplete="off"
        spellCheck="false"
        {...(locked ? {} : { placeholder: "KB5031234" })}
        onChange={(typed) => {
          setText(typed);
          onChange(parseKbs(typed));
        }}
        onBlur={() => {
          setText(null);
        }}
        hint={<Trans>KB numbers, separated by commas.</Trans>}
        isInvalid={problems.length > 0}
        errorMessage={problems.join(" ")}
      />
    </div>
  );
}
