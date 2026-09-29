// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Checkbox } from "@/ui/Checkbox";
import { StateTag } from "@/ui/StateTag";

import { isListed, withInterface, type PxeHostInterfaces } from "../networkBoot";

// One host's interfaces, each with a checkbox that adds it to the list or removes it.
export function HostInterfaces({
  host,
  entries,
  editable,
  onChange,
}: {
  host: PxeHostInterfaces;
  entries: string[];
  editable: boolean;
  onChange: (entries: string[]) => void;
}) {
  const { t } = useLingui();
  const name = host.host;

  return (
    <div className="flex flex-col gap-1.5">
      <span className="type-label text-ink">
        <Trans>On {name}</Trans>
      </span>
      {host.interfaces.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>This host found no interface with an IPv4 address.</Trans>
        </p>
      ) : (
        <ul aria-label={t`Interfaces on ${name}`} className="flex flex-col">
          {host.interfaces.map((candidate) => (
            <li
              key={candidate.name}
              className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line-soft py-1.5 last:border-b-0"
            >
              <Checkbox
                isSelected={isListed(entries, candidate)}
                onChange={(serve) => {
                  onChange(withInterface(entries, candidate, serve));
                }}
                isReadOnly={!editable}
              >
                <span className="type-data">{candidate.name}</span>
              </Checkbox>
              <span className="min-w-0 flex-1 type-data break-words text-muted">
                {candidate.addresses.join(", ")}
              </span>
              {candidate.served ? (
                <StateTag tone="ok">
                  <Trans>Serving</Trans>
                </StateTag>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
