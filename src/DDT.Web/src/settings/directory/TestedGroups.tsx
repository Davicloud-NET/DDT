// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { roleLabel } from "@/users/userView";

import { entryOf } from "../signIn";

// The tested user's groups in a collapsed list, each with the role the map gives it.
export function TestedGroups({ groups, map }: { groups: string[]; map: Record<string, string> }) {
  const count = groups.length;

  if (count === 0) {
    return (
      <span className="text-muted">
        <Trans>None</Trans>
      </span>
    );
  }

  return (
    <details>
      <summary className="cursor-pointer text-ink-2">
        {plural(count, { one: "# group", other: "# groups" })}
      </summary>
      <ul className="mt-1 flex flex-col gap-0.5">
        {groups.map((group) => (
          <GroupLine key={group} group={group} role={entryOf(map, group)} />
        ))}
      </ul>
    </details>
  );
}

function GroupLine({ group, role }: { group: string; role: string | undefined }) {
  const label = role === undefined ? null : roleLabel(role);

  return (
    <li className="type-data text-[12.5px] break-all text-muted">
      {group}
      {label === null ? null : (
        <span className="ml-2 font-sans type-small text-ink">
          <Trans>gives {label}</Trans>
        </span>
      )}
    </li>
  );
}
