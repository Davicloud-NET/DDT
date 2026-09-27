// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useContext, useId, useState, type ReactNode } from "react";

import { Checkbox } from "@/ui/Checkbox";
import { NumberField } from "@/ui/Controls";
import { cx } from "@/ui/cx";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "./editorLock";
import { fieldFindings, type Findings } from "./problems";
import { isInt32, parseCodes } from "./steps";

// The fields of a step, on the components of the design system. Each one shows the server's findings for its
// field: a problem marks it invalid and is said under it, a warning is said under it in the attention colour. Each
// sits in an element named by its field, so the list of findings can take the focus there. Read only, a choice
// shows as text, since a disabled list is hard to read.

export interface FieldBase {
  label: ReactNode;
  // The field in the step, such as "script" or "conditions[1].value", which the server's findings name.
  field: string;
  findings: Findings;
  hint?: ReactNode;
  className?: string;
}

function findingProps(findings: Findings, field: string, hint: ReactNode) {
  const { problems, warnings } = fieldFindings(findings, field);
  const warning = warnings.join(" ");

  return {
    isInvalid: problems.length > 0,
    errorMessage: problems.join(" "),
    hint:
      warning === "" ? (
        hint
      ) : (
        <>
          {hint ? <>{hint} </> : null}
          <span className="text-attention-text">{warning}</span>
        </>
      ),
  };
}

export function TextSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
  placeholder,
  mono = false,
  multiline = false,
  rows,
  maxLength,
}: FieldBase & {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  mono?: boolean;
  multiline?: boolean;
  rows?: number;
  maxLength?: number;
}) {
  const locked = useContext(EditorLock);

  return (
    <div data-field={field} className={className}>
      <TextField
        label={label}
        value={value}
        onChange={onChange}
        isReadOnly={locked}
        mono={mono}
        multiline={multiline}
        spellCheck={mono ? "false" : "true"}
        autoComplete="off"
        {...(rows === undefined ? {} : { rows })}
        {...(placeholder === undefined || locked ? {} : { placeholder })}
        {...(maxLength === undefined ? {} : { maxLength })}
        {...findingProps(findings, field, hint)}
      />
    </div>
  );
}

export interface Choice {
  id: string;
  label: string;
  description?: string;
  isDisabled?: boolean;
}

export function ChoiceSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  choices,
  onChange,
  placeholder,
}: FieldBase & {
  // Null shows the placeholder.
  value: string | null;
  choices: Choice[];
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const locked = useContext(EditorLock);
  const findingsOf = findingProps(findings, field, hint);

  if (locked) {
    const chosen = choices.find((choice) => choice.id === value);

    return (
      <div data-field={field} className={className}>
        <TextField
          label={label}
          value={chosen?.label ?? placeholder ?? ""}
          isReadOnly
          {...findingsOf}
        />
      </div>
    );
  }

  return (
    <div data-field={field} className={className}>
      <Select
        label={label}
        value={value}
        onChange={(key) => {
          if (key !== null) {
            onChange(String(key));
          }
        }}
        {...(placeholder === undefined ? {} : { placeholder })}
        {...findingsOf}
      >
        {choices.map((choice) => (
          <ListBoxItem
            key={choice.id}
            id={choice.id}
            textValue={choice.label}
            isDisabled={choice.isDisabled === true}
            {...(choice.description === undefined ? {} : { description: choice.description })}
          >
            {choice.label}
          </ListBoxItem>
        ))}
      </Select>
    </div>
  );
}

// A whole number the server can store. The keys step it, and a number past the bounds is brought within them when
// the field is left.
export function NumberSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
  minValue,
  maxValue = 2_147_483_647,
}: FieldBase & {
  value: number;
  onChange: (value: number) => void;
  minValue: number;
  maxValue?: number;
}) {
  const locked = useContext(EditorLock);

  return (
    <div data-field={field} className={className}>
      <NumberField
        label={label}
        value={value}
        minValue={minValue}
        maxValue={maxValue}
        step={1}
        formatOptions={{ useGrouping: false, maximumFractionDigits: 0 }}
        isReadOnly={locked}
        onChange={(number) => {
          if (isInt32(number)) {
            onChange(number);
          }
        }}
        {...findingProps(findings, field, hint)}
      />
    </div>
  );
}

// Exit codes as a list such as "0, 3010". What was typed stays while the field is typed in, even when it is no list
// yet; the step keeps the last list that was one.
export function CodesSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
}: FieldBase & { value: readonly number[]; onChange: (value: number[]) => void }) {
  const locked = useContext(EditorLock);
  const [text, setText] = useState<string | null>(null);
  const invalid = text !== null && parseCodes(text) === null;
  const findingsOf = findingProps(findings, field, hint);

  return (
    <div data-field={field} className={className}>
      <TextField
        label={label}
        value={text ?? value.join(", ")}
        isReadOnly={locked}
        mono
        inputMode="numeric"
        autoComplete="off"
        spellCheck="false"
        onChange={(typed) => {
          const codes = parseCodes(typed);

          setText(typed);

          if (codes !== null) {
            onChange(codes);
          }
        }}
        onBlur={() => {
          setText(null);
        }}
        hint={findingsOf.hint}
        isInvalid={invalid || findingsOf.isInvalid}
        errorMessage={
          invalid
            ? t`Enter whole numbers from -2147483648 to 2147483647, separated by commas. Until then the last list stays.`
            : findingsOf.errorMessage
        }
      />
    </div>
  );
}

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
