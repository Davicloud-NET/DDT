// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { FieldLabel } from "./FieldLabel";
import type { InputFieldProps } from "./inputField";
import { KeyChoice } from "./KeyChoice";

export function YesNoField({ input, draft, hint, error, onChange }: InputFieldProps) {
  const { t } = useLingui();

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
}
