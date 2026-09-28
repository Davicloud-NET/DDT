// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useContext, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { FieldError, Input, Label, Text, TextArea, TextField } from "react-aria-components";

import { cx } from "@/ui/cx";
import { fieldClass } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { complete, completionAt, parseTemplate, renderTemplate } from "../flow/templates";
import { fieldFindings, type Findings } from "../problems";
import { useBuilder } from "./builderData";

// The most names the completion offers at once.
const OFFERED = 8;

// A text setting that is a template, such as a computer name made of PC-{{SerialNumber|alnum|right:12}}. Typing {{
// offers the names the sequence knows, Up and Down choose one and Enter or Tab puts it in. Under the field, the
// template is filled in for a sample machine, and a name nothing defines is pointed out, before the server does.
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
}: {
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
  // Whether the field says how to use a value, where templates are the point of it.
  howTo?: boolean;
  className?: string;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const { names, known, sample } = useBuilder();
  const input = useRef<HTMLInputElement & HTMLTextAreaElement>(null);
  const listId = useId();
  const [typing, setTyping] = useState<{ from: number; typed: string } | null>(null);
  const [active, setActive] = useState(0);
  const { problems, warnings } = fieldFindings(findings, field);

  const offered =
    typing === null
      ? []
      : [
          ...names.filter((name) => name.toLowerCase().startsWith(typing.typed.toLowerCase())),
          ...names.filter(
            (name) =>
              !name.toLowerCase().startsWith(typing.typed.toLowerCase()) &&
              name.toLowerCase().includes(typing.typed.toLowerCase()),
          ),
        ].slice(0, OFFERED);
  const open = !locked && offered.length > 0;
  const chosen = Math.min(active, Math.max(0, offered.length - 1));
  const optionId = (index: number) => `${listId}-${String(index)}`;

  const follow = (text: string) => {
    const caret = input.current?.selectionStart ?? text.length;

    setTyping(completionAt(text, caret));
    setActive(0);
  };

  const insert = (name: string) => {
    const caret = input.current?.selectionStart ?? value.length;
    const done = complete(value, caret, name);

    setTyping(null);

    if (done === null) {
      return;
    }

    onChange(done.text);
    requestAnimationFrame(() => {
      input.current?.setSelectionRange(done.caret, done.caret);
    });
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (!open) {
      return;
    }

    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp":
        event.preventDefault();
        setActive((chosen + (event.key === "ArrowDown" ? 1 : offered.length - 1)) % offered.length);
        break;
      case "Enter":
      case "Tab": {
        const name = offered[chosen];

        if (name !== undefined) {
          event.preventDefault();
          insert(name);
        }

        break;
      }
      case "Escape":
        // The completion closes; the inspector around keeps the focus in the field.
        event.stopPropagation();
        setTyping(null);
        break;
    }
  };

  const parsed = value.includes("{{") ? parseTemplate(value, known) : null;
  const rendered = parsed === null ? null : renderTemplate(value, sample);
  const braces = "{{";
  const example = "{{ComputerName}}";
  const shown = cx(fieldClass, mono ? "type-data" : "type-body", multiline ? "py-2" : "h-9.5");
  const listProps = open
    ? {
        "aria-controls": listId,
        "aria-activedescendant": optionId(chosen),
      }
    : {};

  return (
    <div data-field={field} className={cx("relative", className)}>
      <TextField
        value={value}
        onChange={(text) => {
          onChange(text);
          follow(text);
        }}
        onBlur={() => {
          setTyping(null);
        }}
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
            onKeyDown={onKeyDown}
            {...listProps}
            {...(placeholder === undefined || locked ? {} : { placeholder })}
            className={cx(shown, "resize-y")}
          />
        ) : (
          <Input
            ref={input}
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={open}
            onKeyDown={onKeyDown}
            {...listProps}
            {...(placeholder === undefined || locked ? {} : { placeholder })}
            className={shown}
          />
        )}
        <Text slot="description" className="flex flex-col gap-0.5 type-small text-muted">
          {hint ? <span>{hint}</span> : null}
          {locked || !howTo ? null : (
            <span>{t`Type ${braces} to use a value, such as ${example}.`}</span>
          )}
          {parsed?.problems.map((problem) => (
            <span key={problem.message} className="text-attention-text">
              {problem.message}
            </span>
          ))}
          {rendered !== null && parsed?.problems.length === 0 ? (
            rendered.error === null ? (
              <span>
                {t`On a sample machine:`}{" "}
                <span className="type-data text-ink-2">{rendered.output}</span>
              </span>
            ) : (
              <span className="text-attention-text">{rendered.error.message}</span>
            )
          ) : null}
          {warnings.map((warning) => (
            <span key={warning} className="text-attention-text">
              {warning}
            </span>
          ))}
        </Text>
        <FieldError className="type-small text-fail-text">{problems.join(" ")}</FieldError>
      </TextField>
      {open ? (
        <ul
          id={listId}
          role="listbox"
          aria-label={t`Values to use`}
          className="absolute inset-x-0 top-full z-30 mt-1 max-h-64 overflow-auto rounded-overlay bg-raised p-1 shadow-overlay entering:animate-pop-in"
        >
          {offered.map((name, index) => (
            <li
              key={name}
              id={optionId(index)}
              role="option"
              aria-selected={index === chosen}
              className={cx(
                "cursor-pointer rounded-key px-2.5 py-1.5 type-data text-ink motion-highlight",
                index === chosen && "bg-hover",
              )}
              onMouseDown={(event) => {
                // The field keeps the focus.
                event.preventDefault();
              }}
              onClick={() => {
                insert(name);
              }}
            >
              {name}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
