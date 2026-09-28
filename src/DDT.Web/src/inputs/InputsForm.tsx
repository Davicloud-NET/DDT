// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import type { ReactNode } from "react";
import {
  CheckboxGroup,
  FieldError,
  Label,
  RadioButton,
  RadioField,
  RadioGroup,
  Text,
} from "react-aria-components";

import { Checkbox } from "@/ui/Checkbox";
import { cx } from "@/ui/cx";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";
import { prefillText, type ResolvedValue } from "@/values/values";

import {
  destinationText,
  errorFor,
  prefilledBy,
  type AnswerDraft,
  type AskedInput,
} from "./inputs";
import type { Answers } from "./useAnswers";

// A choice of up to this many answers is a row of keys; a longer one is a list to open.
const KEYS_AT_MOST = 4;

// The fields of a sequence's inputs, one per input by its kind: text, a choice as a row of keys or a list, several
// choices, yes or no, and an account as a user name and a password with where the account is used. A field says where
// its first value came from, marks a required answer, and shows under it what the page or the server refused. A
// password is never shown again.
export function InputsForm({
  inputs,
  answers,
  defaults = [],
  errors = {},
}: {
  inputs: readonly AskedInput[];
  answers: Answers;
  defaults?: readonly ResolvedValue[];
  // The server's refusals by input name.
  errors?: Record<string, string>;
}) {
  return (
    <div className="flex flex-col gap-4">
      {inputs.map((input) => {
        const prefilled = prefilledBy(input, defaults);
        const hint = [input.help, prefilled === null ? null : prefillText(prefilled)]
          .filter((part): part is string => part !== null && part !== "")
          .join(" ");

        return (
          <InputField
            key={input.name}
            input={input}
            draft={answers.drafts[input.name]}
            hint={hint === "" ? null : hint}
            error={errorFor(answers.missing, input.name) ?? errorFor(errors, input.name)}
            onChange={(draft) => {
              answers.edit(input.name, draft);
            }}
          />
        );
      })}
    </div>
  );
}

// The input's label, marked where an answer is required.
function FieldLabel({ input }: { input: AskedInput }) {
  const label = input.label;

  return input.required ? (
    <Trans>
      {label} <span className="font-normal text-muted">(required)</span>
    </Trans>
  ) : (
    label
  );
}

const emptyDraft: AnswerDraft = { value: "", values: [], userName: "", password: "" };

function InputField({
  input,
  draft = emptyDraft,
  hint,
  error,
  onChange,
}: {
  input: AskedInput;
  draft?: AnswerDraft | undefined;
  hint: string | null;
  error: string | null;
  onChange: (draft: AnswerDraft) => void;
}) {
  const { t } = useLingui();
  const invalid = error !== null;
  const choices = input.choices.map((choice) => ({
    value: choice.value,
    label: choice.label ?? choice.value,
  }));

  switch (input.kind) {
    case "Text":
      return (
        <TextField
          label={<FieldLabel input={input} />}
          value={draft.value}
          onChange={(value) => {
            onChange({ ...draft, value });
          }}
          isRequired={input.required}
          isInvalid={invalid}
          errorMessage={error}
          {...(input.maxLength === null ? {} : { maxLength: input.maxLength })}
          {...(hint === null ? {} : { hint })}
          autoComplete="off"
        />
      );
    case "Choice":
      return choices.length <= KEYS_AT_MOST ? (
        <KeyChoice
          label={<FieldLabel input={input} />}
          choices={choices}
          value={draft.value}
          onChange={(value) => {
            onChange({ ...draft, value });
          }}
          isRequired={input.required}
          hint={hint}
          error={error}
        />
      ) : (
        <Select
          label={<FieldLabel input={input} />}
          value={draft.value === "" ? null : draft.value}
          onChange={(key) => {
            onChange({ ...draft, value: key === null ? "" : String(key) });
          }}
          placeholder={t`Choose an answer`}
          isRequired={input.required}
          isInvalid={invalid}
          errorMessage={error}
          {...(hint === null ? {} : { hint })}
        >
          {choices.map((choice) => (
            <ListBoxItem key={choice.value} id={choice.value}>
              {choice.label}
            </ListBoxItem>
          ))}
        </Select>
      );
    case "MultiChoice":
      return (
        <CheckboxGroup
          value={draft.values}
          onChange={(values) => {
            onChange({ ...draft, values });
          }}
          isRequired={input.required}
          isInvalid={invalid}
          className="flex flex-col gap-1.5"
        >
          <Label className="type-label text-ink">
            <FieldLabel input={input} />
          </Label>
          <div className="flex flex-col gap-1.5 pt-0.5">
            {choices.map((choice) => (
              <Checkbox key={choice.value} value={choice.value}>
                {choice.label}
              </Checkbox>
            ))}
          </div>
          {hint !== null ? (
            <Text slot="description" className="type-small text-muted">
              {hint}
            </Text>
          ) : null}
          <FieldError className="type-small text-fail-text">{error}</FieldError>
        </CheckboxGroup>
      );
    case "YesNo":
      return (
        <KeyChoice
          label={<FieldLabel input={input} />}
          choices={[
            { value: "true", label: t`Yes` },
            { value: "false", label: t`No` },
          ]}
          value={draft.value.toLowerCase()}
          onChange={(value) => {
            onChange({ ...draft, value });
          }}
          isRequired={input.required}
          hint={hint}
          error={error}
        />
      );
    case "Account": {
      const destination = destinationText(input.account);
      const label = input.label;

      return (
        <fieldset className="flex flex-col gap-3 rounded-key bg-well px-3.5 pt-2.5 pb-3.5">
          <legend className="float-left pt-1 type-label text-ink">
            <FieldLabel input={input} />
          </legend>
          <span className="clear-both -mt-1.5 flex flex-col gap-1 type-small text-muted">
            <span>
              <Trans>
                An account for this run only, which DDT uses and never shows or hands to a script.
              </Trans>
            </span>
            {destination !== null ? <span>{destination}</span> : null}
            {input.help !== null && input.help !== "" ? <span>{input.help}</span> : null}
          </span>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField
              label={t`User name for ${label}`}
              value={draft.userName}
              onChange={(userName) => {
                onChange({ ...draft, userName });
              }}
              isRequired={input.required}
              isInvalid={invalid}
              autoComplete="off"
              spellCheck="false"
              mono
            />
            <TextField
              label={t`Password for ${label}`}
              type="password"
              value={draft.password}
              onChange={(password) => {
                onChange({ ...draft, password });
              }}
              isRequired={input.required}
              isInvalid={invalid}
              autoComplete="new-password"
            />
          </div>
          {error !== null ? (
            <span role="alert" className="type-small text-fail-text">
              {error}
            </span>
          ) : null}
        </fieldset>
      );
    }
  }
}

// A choice among a few answers as a row of keys, the chosen one raised out of its well, as the filters are.
function KeyChoice({
  label,
  choices,
  value,
  onChange,
  isRequired,
  hint,
  error,
}: {
  label: ReactNode;
  choices: { value: string; label: string }[];
  value: string;
  onChange: (value: string) => void;
  isRequired: boolean;
  hint: string | null;
  error: string | null;
}) {
  return (
    <RadioGroup
      value={value === "" ? null : value}
      onChange={onChange}
      isRequired={isRequired}
      isInvalid={error !== null}
      orientation="horizontal"
      className="flex flex-col gap-1.5"
    >
      <Label className="type-label text-ink">{label}</Label>
      <div className="flex w-fit max-w-full flex-wrap gap-0.5 rounded-panel bg-well p-0.75 shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
        {choices.map((choice) => (
          <RadioField key={choice.value} value={choice.value} className="flex">
            <RadioButton
              className={cx(
                "flex h-8 cursor-pointer items-center rounded-key px-2.75 type-label font-semibold text-ink-2 motion-colors outline-none",
                "hover:text-ink selected:bg-raised selected:text-ink selected:shadow-[0_0_0_1px_var(--color-line)]",
                "focus-visible:outline-2 focus-visible:outline-focus",
              )}
            >
              {choice.label}
            </RadioButton>
          </RadioField>
        ))}
      </div>
      {hint !== null ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{error}</FieldError>
    </RadioGroup>
  );
}
