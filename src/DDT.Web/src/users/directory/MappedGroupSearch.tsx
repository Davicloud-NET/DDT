// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { DirectoryGroupSearch } from "../DirectoryGroupSearch";
import type { DirectoryView } from "../users";
import { roleLabel } from "../userView";

// The group search, with the role the map gives each group found.
export function MappedGroupSearch({ directory }: { directory: DirectoryView }) {
  const mapped = new Map(
    directory.groupRoleMap.map((entry) => [entry.group.toLowerCase(), entry.role]),
  );

  return (
    <DirectoryGroupSearch
      heading="h3"
      className="w-full"
      idle={
        <p className="type-small text-muted">
          <Trans>Type part of a group's name to see its distinguished name for the map.</Trans>
        </p>
      }
      renderGroup={(group) => {
        const role = mapped.get(group.distinguishedName.toLowerCase());
        const label = role === undefined ? null : roleLabel(role);

        return (
          <li key={group.distinguishedName} className="flex flex-col gap-0.5 px-3 py-2">
            <span className="flex items-baseline gap-2">
              <span className="min-w-0 flex-1 truncate type-label text-ink">
                {group.name ?? group.distinguishedName}
              </span>
              {label !== null ? (
                <span className="shrink-0 type-small text-ink-2">
                  <Trans>Gives {label}</Trans>
                </span>
              ) : null}
            </span>
            <span className="type-data text-[12.5px] break-all text-muted select-all">
              {group.distinguishedName}
            </span>
            {group.description !== null && group.description !== "" ? (
              <span className="type-small text-ink-2">{group.description}</span>
            ) : null}
          </li>
        );
      }}
    />
  );
}
