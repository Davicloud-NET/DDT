// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { ListBoxItem, Select } from "@/ui/Select";

import { FieldLabel } from "./FieldLabel";
import { choicesOf, type InputFieldProps } from "./inputField";
import { KeyChoice } from "./KeyChoice";

// A choice with up to this many answers shows as a row of buttons. A longer one shows as a list to open.
const KEYS_AT_MOST = 4;

export function ChoiceField({ input, draft, hint, error, onChange }: InputFieldProps) {
  const { t } = useLingui();
  const choices = choicesOf(input);

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
      isInvalid={error !== null}
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
}
