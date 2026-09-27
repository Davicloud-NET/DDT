// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useState, type ReactNode } from "react";

import { roleDescription, roleLabel, ROLES } from "@/users/userView";
import { Button } from "@/ui/Button";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { hasEntry } from "./signIn";

// The role a stored value names, whatever its case, such as "administrator" from configuration.
function canonicalRole(role: string): string {
  return ROLES.find((known) => known.toLowerCase() === role.trim().toLowerCase()) ?? role;
}

// A map from something the sign-in brings along, a directory group or a claim value, to the role it gives: one row
// per entry with its role and a key that removes it, and a field that adds an entry by typing it. An entry added
// here gives Viewer until another role is chosen in its row.
export function RoleMapEditor({
  label,
  map,
  onChange,
  canChange,
  errorsOf,
  describe,
  empty,
  addLabel,
  addHint,
  addPlaceholder,
  addAction,
  duplicate,
}: {
  // The name of the list, for screen readers.
  label: string;
  map: Record<string, string>;
  onChange: (map: Record<string, string>) => void;
  canChange: boolean;
  // The server's refusals and the stored section's problems for one entry.
  errorsOf: (key: string) => string[];
  // What the page knows about an entry besides its key, such as the group's name in the directory.
  describe?: (key: string) => ReactNode;
  empty: ReactNode;
  addLabel: ReactNode;
  addHint?: ReactNode;
  addPlaceholder: string;
  addAction: ReactNode;
  duplicate: string;
}) {
  const { t } = useLingui();
  const [typed, setTyped] = useState("");
  const [refused, setRefused] = useState<string | null>(null);
  const entries = Object.entries(map);

  const add = () => {
    const key = typed.trim();

    if (key === "") {
      return;
    }

    if (hasEntry(map, key)) {
      setRefused(duplicate);
      return;
    }

    onChange({ ...map, [key]: "Viewer" });
    setTyped("");
    setRefused(null);
  };

  return (
    <div className="flex flex-col gap-3">
      {entries.length === 0 ? (
        <p className="type-small text-muted">{empty}</p>
      ) : (
        <ul
          aria-label={label}
          className="flex flex-col divide-y divide-line-soft rounded-key shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
        >
          {entries.map(([key, role]) => {
            const errors = errorsOf(key);
            const shown = canonicalRole(role);
            const known = (ROLES as readonly string[]).includes(shown);

            return (
              <li key={key} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-3.5 py-2.5">
                <span className="flex min-w-48 flex-1 flex-col gap-0.5">
                  {describe?.(key)}
                  <span className="type-data text-[12.5px] break-all text-ink">{key}</span>
                  {errors.length > 0 ? (
                    <span className="type-small text-fail-text">{errors.join(" ")}</span>
                  ) : null}
                </span>
                <Select
                  label={<span className="sr-only">{t`Role that ${key} gives`}</span>}
                  value={shown}
                  onChange={(next) => {
                    if (next !== null) {
                      onChange({ ...map, [key]: String(next) });
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
                      onChange(Object.fromEntries(entries.filter(([other]) => other !== key)));
                    }}
                  >
                    <Trans>Remove</Trans>
                  </Button>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
      {canChange ? (
        <form
          className="flex flex-wrap items-start gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            add();
          }}
        >
          <TextField
            label={addLabel}
            {...(addHint === undefined ? {} : { hint: addHint })}
            placeholder={addPlaceholder}
            mono
            autoComplete="off"
            spellCheck="false"
            value={typed}
            onChange={(value) => {
              setTyped(value);
              setRefused(null);
            }}
            isInvalid={refused !== null}
            errorMessage={refused}
            className="min-w-64 flex-1"
          />
          <Button type="submit" isDisabled={typed.trim() === ""} className="mt-6.5">
            {addAction}
          </Button>
        </form>
      ) : null}
    </div>
  );
}
