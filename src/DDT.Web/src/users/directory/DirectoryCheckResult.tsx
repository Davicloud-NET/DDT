// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";
import { Notice } from "@/ui/Notice";
import { ResultBox } from "@/ui/ResultBox";

import { directoryCheckText, type DirectoryCheck } from "../users";
import { roleLabel } from "../userView";
import { firstValue } from "./distinguishedName";

export function DirectoryCheckResult({ result }: { result: DirectoryCheck }) {
  if (!result.found) {
    return <Notice tone="attention">{directoryCheckText(result)}</Notice>;
  }

  const count = result.groups.length;
  const role = result.role === null ? null : roleLabel(result.role);

  return (
    <ResultBox>
      <Facts
        items={[
          ...(result.displayName === null
            ? []
            : [{ label: <Trans>Name</Trans>, value: result.displayName }]),
          {
            label: <Trans>Entry</Trans>,
            value: result.distinguishedName ?? "",
            mono: true,
          },
          {
            label: <Trans>Role at sign-in</Trans>,
            value:
              role === null ? (
                <span className="text-attention-text">
                  <Trans>None</Trans>
                </span>
              ) : (
                <span className="type-label">{role}</span>
              ),
          },
          {
            label: <Trans>Mapped groups</Trans>,
            value:
              result.matches.length === 0 ? (
                <span className="text-muted">
                  <Trans>None</Trans>
                </span>
              ) : (
                <ul className="flex flex-col gap-0.5">
                  {result.matches.map((match) => (
                    <MatchLine key={match.group} group={match.group} role={match.role} />
                  ))}
                </ul>
              ),
          },
          {
            label: <Trans>All groups</Trans>,
            value:
              count === 0 ? (
                <span className="text-muted">
                  <Trans>None</Trans>
                </span>
              ) : (
                <details>
                  <summary className="cursor-pointer text-ink-2">
                    {plural(count, { one: "# group", other: "# groups" })}
                  </summary>
                  <ul className="mt-1 flex flex-col gap-0.5">
                    {result.groups.map((group) => (
                      <li key={group} className="type-data text-[12.5px] break-all text-muted">
                        {group}
                      </li>
                    ))}
                  </ul>
                </details>
              ),
          },
        ]}
      />
      <p className="type-small text-ink">{directoryCheckText(result)}</p>
    </ResultBox>
  );
}

function MatchLine({ group, role }: { group: string; role: string }) {
  const name = firstValue(group);
  const label = roleLabel(role);

  return (
    <li title={group}>
      <Trans>
        {name} gives {label}
      </Trans>
    </li>
  );
}
