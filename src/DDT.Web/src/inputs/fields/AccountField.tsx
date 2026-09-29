// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { destinationText } from "@/inputs/inputs";
import { TextField } from "@/ui/TextField";

import { FieldLabel } from "./FieldLabel";
import type { InputFieldProps } from "./inputField";

// A user name and a password, with where the account is used. The password is never shown again.
export function AccountField({ input, draft, error, onChange }: InputFieldProps) {
  const { t } = useLingui();
  const invalid = error !== null;
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
