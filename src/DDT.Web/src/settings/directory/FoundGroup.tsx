// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import type { DirectoryGroup } from "@/users/users";
import { roleLabel } from "@/users/userView";

import { entryOf } from "../signIn";

// A group the search found, with the role the map gives it or a key that adds it.
export function FoundGroup({
  group,
  map,
  onAdd,
}: {
  group: DirectoryGroup;
  map: Record<string, string>;
  onAdd: (group: string, name: string | null) => void;
}) {
  const { t } = useLingui();
  const dn = group.distinguishedName;
  const shown = group.name ?? dn;
  const role = entryOf(map, dn);
  const label = role === undefined ? null : roleLabel(role);

  return (
    <li className="flex items-center gap-3 px-3 py-2">
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="truncate type-label text-ink">{shown}</span>
        <span className="type-data text-[12.5px] break-all text-muted select-all">{dn}</span>
        {group.description !== null && group.description !== "" ? (
          <span className="type-small text-ink-2">{group.description}</span>
        ) : null}
      </span>
      {label !== null ? (
        <span className="shrink-0 type-small text-ink-2">
          <Trans>Gives {label}</Trans>
        </span>
      ) : (
        <Button
          size="sm"
          aria-label={t`Add ${shown} to the map`}
          onPress={() => {
            onAdd(dn, group.name);
          }}
        >
          <Trans>Add</Trans>
        </Button>
      )}
    </li>
  );
}
