// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useContext, type ReactNode } from "react";

import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { fieldFindings, type Findings } from "../problems";
import type { AccountReference } from "../sequences";
import { NONE, referenceOf, type AccountUse } from "./account/accountChoices";
import { AccountSection } from "./account/AccountSection";
import { useAccountChoices } from "./account/useAccountChoices";

interface AccountSettingProps {
  label: ReactNode;
  field: string;
  findings: Findings;
  hint?: ReactNode;
  value: AccountReference | null;
  use: AccountUse;
  // The label for choosing no account, if the step may have none.
  noneLabel?: string;
  onChange: (value: AccountReference | null) => void;
  className?: string;
}

// Chooses the account a step uses: one stored on the server, or an Account input of this sequence that is asked for
// the run.
export function AccountSetting({
  label,
  field,
  findings,
  hint,
  value,
  use,
  noneLabel,
  onChange,
  className,
}: AccountSettingProps) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const { key, missing, chosenLabel, noneStored, storedItems, askedItems } = useAccountChoices(
    value,
    use,
    noneLabel,
  );
  const { problems, warnings } = fieldFindings(findings, field);
  const described = [
    hint,
    noneStored && !locked ? t`No account is stored on the server yet.` : null,
    ...warnings,
  ].filter((part) => part !== null && part !== undefined);
  const description =
    described.length === 0 ? undefined : (
      <>
        {described.map((part, index) => (
          <span key={index} className={index === 0 && hint !== undefined ? undefined : "block"}>
            {part}{" "}
          </span>
        ))}
      </>
    );

  if (locked) {
    return (
      <div data-field={field} className={className}>
        <TextField
          label={label}
          value={chosenLabel}
          isReadOnly
          isInvalid={problems.length > 0}
          errorMessage={problems.join(" ")}
          {...(description === undefined ? {} : { hint: description })}
        />
      </div>
    );
  }

  return (
    <div data-field={field} className={className}>
      <Select
        label={label}
        value={key === NONE && noneLabel === undefined ? null : key}
        placeholder={t`Choose an account`}
        isInvalid={problems.length > 0}
        errorMessage={problems.join(" ")}
        {...(description === undefined ? {} : { hint: description })}
        onChange={(chosen) => {
          const text = String(chosen);

          if (text !== key) {
            onChange(referenceOf(text));
          }
        }}
      >
        {noneLabel === undefined ? null : (
          <ListBoxItem id={NONE} textValue={noneLabel}>
            {noneLabel}
          </ListBoxItem>
        )}
        {missing === null ? null : (
          <ListBoxItem id={key} textValue={missing}>
            {missing}
          </ListBoxItem>
        )}
        <AccountSection title={t`Stored accounts`} items={storedItems} />
        <AccountSection title={t`Asked for the run`} items={askedItems} />
      </Select>
    </div>
  );
}
