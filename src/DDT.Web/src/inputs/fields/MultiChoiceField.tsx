// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState } from "react";
import { CheckboxGroup, FieldError, Label, Text } from "react-aria-components";

import { Checkbox } from "@/ui/Checkbox";
import { SearchField } from "@/ui/SearchField";

import { FieldLabel } from "./FieldLabel";
import { choicesOf, matchingChoices, SEARCH_ABOVE, type InputFieldProps } from "./inputField";

// A long list gets a search. A choice the search hides stays ticked.
export function MultiChoiceField({ input, draft, hint, error, onChange }: InputFieldProps) {
  const { t } = useLingui();
  const [search, setSearch] = useState("");
  const choices = choicesOf(input);
  const long = choices.length > SEARCH_ABOVE;
  const shown = long ? matchingChoices(choices, search) : choices;
  const label = input.label;
  const count = draft.values.length;

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
      {long ? (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <SearchField
            label={t`Search ${label}`}
            placeholder={t`Search`}
            value={search}
            onChange={setSearch}
            className="w-64"
          />
          <span className="type-small text-muted">{t`${count} chosen`}</span>
        </div>
      ) : null}
      <div className="flex flex-col gap-1.5 pt-0.5">
        {shown.map((choice) => (
          <Checkbox key={choice.value} value={choice.value}>
            {choice.label}
          </Checkbox>
        ))}
        {shown.length === 0 && long ? (
          <span className="type-small text-muted">
            <Trans>No choice matches.</Trans>
          </span>
        ) : null}
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
