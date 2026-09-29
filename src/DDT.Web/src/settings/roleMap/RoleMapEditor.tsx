// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { RoleMapAddForm } from "./RoleMapAddForm";
import { RoleMapEntry } from "./RoleMapEntry";
import { useRoleMapAdd } from "./useRoleMapAdd";

interface RoleMapEditorProps {
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
}

// Maps something the sign-in brings along, like a directory group or a claim value, to the role it gives. A new entry
// gives Viewer until another role is picked in its row.
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
}: RoleMapEditorProps) {
  const adding = useRoleMapAdd(map, onChange, duplicate);
  const entries = Object.entries(map);

  return (
    <div className="flex flex-col gap-3">
      {entries.length === 0 ? (
        <p className="type-small text-muted">{empty}</p>
      ) : (
        <ul
          aria-label={label}
          className="flex flex-col divide-y divide-line-soft rounded-key shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
        >
          {entries.map(([key, role]) => (
            <RoleMapEntry
              key={key}
              entryKey={key}
              role={role}
              errors={errorsOf(key)}
              description={describe?.(key)}
              canChange={canChange}
              onChange={(next) => {
                onChange({ ...map, [key]: next });
              }}
              onRemove={() => {
                onChange(Object.fromEntries(entries.filter(([other]) => other !== key)));
              }}
            />
          ))}
        </ul>
      )}
      {canChange ? (
        <RoleMapAddForm
          adding={adding}
          label={addLabel}
          hint={addHint}
          placeholder={addPlaceholder}
          action={addAction}
        />
      ) : null}
    </div>
  );
}
