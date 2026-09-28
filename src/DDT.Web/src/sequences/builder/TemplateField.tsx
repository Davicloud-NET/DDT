// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext, useRef, type ReactNode } from "react";
import { FieldError, Input, Label, TextArea, TextField } from "react-aria-components";

import { cx } from "@/ui/cx";
import { fieldClass } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { fieldFindings, type Findings } from "../problems";
import { useBuilder } from "./builderData";
import { CompletionList } from "./template/CompletionList";
import { TemplateDescription } from "./template/TemplateDescription";
import { useTemplateCompletion } from "./template/useTemplateCompletion";

interface TemplateFieldProps {
  label: ReactNode;
  // The field in the node or the declaration, such as "value" or "shares[0].path".
  field: string;
  findings: Findings;
  hint?: ReactNode;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  multiline?: boolean;
  rows?: number;
  mono?: boolean;
  // Whether the field explains how to use a value. Set it where templates are the point of the field.
  howTo?: boolean;
  className?: string;
}

// A text setting that's a template, such as a computer name made of PC-{{SerialNumber|alnum|right:12}}. It completes
// the names the sequence knows and shows a preview for a sample machine.
export function TemplateField({
  label,
  field,
  findings,
  hint,
  value,
  onChange,
  placeholder,
  multiline = false,
  rows = 3,
  mono = true,
  howTo = true,
  className,
}: TemplateFieldProps) {
  const locked = useContext(EditorLock);
  const { names, known, sample } = useBuilder();
  const input = useRef<HTMLInputElement & HTMLTextAreaElement>(null);
  const completion = useTemplateCompletion({ input, value, onChange, names, locked });
  const { problems, warnings } = fieldFindings(findings, field);
  const shown = cx(fieldClass, mono ? "type-data" : "type-body", multiline ? "py-2" : "h-9.5");
  const shownPlaceholder = placeholder === undefined || locked ? {} : { placeholder };

  return (
    <div data-field={field} className={cx("relative", className)}>
      <TextField
        value={value}
        onChange={(text) => {
          onChange(text);
          completion.follow(text);
        }}
        onBlur={completion.close}
        isReadOnly={locked}
        isInvalid={problems.length > 0}
        className="flex flex-col gap-1.5"
        autoComplete="off"
        spellCheck="false"
      >
        <Label className="type-label text-ink">{label}</Label>
        {multiline ? (
          <TextArea
            ref={input}
            rows={rows}
            onKeyDown={completion.onKeyDown}
            {...completion.listProps}
            {...shownPlaceholder}
            className={cx(shown, "resize-y")}
          />
        ) : (
          <Input
            ref={input}
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={completion.open}
            onKeyDown={completion.onKeyDown}
            {...completion.listProps}
            {...shownPlaceholder}
            className={shown}
          />
        )}
        <TemplateDescription
          value={value}
          hint={hint}
          howTo={howTo}
          locked={locked}
          known={known}
          sample={sample}
          warnings={warnings}
        />
        <FieldError className="type-small text-fail-text">{problems.join(" ")}</FieldError>
      </TextField>
      {completion.open ? (
        <CompletionList
          listId={completion.listId}
          offered={completion.offered}
          chosen={completion.chosen}
          optionId={completion.optionId}
          onInsert={completion.insert}
        />
      ) : null}
    </div>
  );
}
