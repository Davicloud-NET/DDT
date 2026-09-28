// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useId } from "react";

import { Checkbox } from "@/ui/Checkbox";
import { cx } from "@/ui/cx";

import { EditorLock } from "../editorLock";
import { fieldFindings } from "../problems";
import type { FieldBase } from "./fieldBase";

// A switch of the step, such as "Go on when this step fails", with what it does under it.
export function FlagSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
}: FieldBase & { value: boolean; onChange: (value: boolean) => void }) {
  const locked = useContext(EditorLock);
  const { problems, warnings } = fieldFindings(findings, field);
  const describedBy = useId();
  const described = hint !== undefined || problems.length > 0 || warnings.length > 0;

  return (
    <div data-field={field} className={cx("flex flex-col gap-1", className)}>
      <Checkbox
        isSelected={value}
        onChange={onChange}
        isReadOnly={locked}
        isInvalid={problems.length > 0}
        {...(described ? { "aria-describedby": describedBy } : {})}
      >
        {label}
      </Checkbox>
      {described ? (
        <span id={describedBy} className="flex flex-col gap-0.5 pl-6.5 type-small">
          {hint ? <span className="text-muted">{hint}</span> : null}
          {problems.map((message) => (
            <span key={message} className="text-fail-text">
              {message}
            </span>
          ))}
          {warnings.map((message) => (
            <span key={message} className="text-attention-text">
              {message}
            </span>
          ))}
        </span>
      ) : null}
    </div>
  );
}
