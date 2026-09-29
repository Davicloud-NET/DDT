// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { CheckboxGroup, FieldError, Label, Text } from "react-aria-components";

import { Checkbox } from "@/ui/Checkbox";

import { FieldLabel } from "./FieldLabel";
import { choicesOf, type InputFieldProps } from "./inputField";

export function MultiChoiceField({ input, draft, hint, error, onChange }: InputFieldProps) {
  return (
    <CheckboxGroup
      value={draft.values}
      onChange={(values) => {
        onChange({ ...draft, values });
      }}
      isRequired={input.required}
      isInvalid={error !== null}
      className="flex flex-col gap-1.5"
    >
      <Label className="type-label text-ink">
        <FieldLabel input={input} />
      </Label>
      <div className="flex flex-col gap-1.5 pt-0.5">
        {choicesOf(input).map((choice) => (
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
}
