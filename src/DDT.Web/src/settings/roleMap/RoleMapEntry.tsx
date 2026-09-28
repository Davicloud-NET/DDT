// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { ListBoxItem, Select } from "@/ui/Select";
import { roleDescription, roleLabel, ROLES } from "@/users/userView";

// The role a stored value names, whatever its case, such as "administrator" from configuration.
function canonicalRole(role: string): string {
  return ROLES.find((known) => known.toLowerCase() === role.trim().toLowerCase()) ?? role;
}

interface RoleMapEntryProps {
  entryKey: string;
  role: string;
  errors: string[];
  description: ReactNode;
  canChange: boolean;
  onChange: (role: string) => void;
  onRemove: () => void;
}

// One row of a role map. A role DDT does not know stays selected as a disabled option until another is chosen.
export function RoleMapEntry({
  entryKey,
  role,
  errors,
  description,
  canChange,
  onChange,
  onRemove,
}: RoleMapEntryProps) {
  const { t } = useLingui();
  const key = entryKey;
  const shown = canonicalRole(role);
  const known = (ROLES as readonly string[]).includes(shown);

  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-3.5 py-2.5">
      <span className="flex min-w-48 flex-1 flex-col gap-0.5">
        {description}
        <span className="type-data text-[12.5px] break-all text-ink">{key}</span>
        <FieldErrorText errors={errors} />
      </span>
      <Select
        label={<span className="sr-only">{t`Role that ${key} gives`}</span>}
        value={shown}
        onChange={(next) => {
          if (next !== null) {
            onChange(String(next));
          }
        }}
        isDisabled={!canChange}
        isInvalid={errors.length > 0}
        className="w-44 gap-0"
      >
        {[
          ...ROLES.map((option) => (
            <ListBoxItem
              key={option}
              id={option}
              textValue={roleLabel(option)}
              description={roleDescription(option)}
            >
              {roleLabel(option)}
            </ListBoxItem>
          )),
          ...(known
            ? []
            : [
                <ListBoxItem key={shown} id={shown} textValue={shown} isDisabled>
                  {shown}
                </ListBoxItem>,
              ]),
        ]}
      </Select>
      {canChange ? (
        <Button
          size="sm"
          variant="quiet"
          aria-label={t`Remove ${key}`}
          onPress={() => {
            onRemove();
          }}
        >
          <Trans>Remove</Trans>
        </Button>
      ) : null}
    </li>
  );
}
