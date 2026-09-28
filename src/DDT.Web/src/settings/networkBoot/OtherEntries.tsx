// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

// The listed entries that name no interface a host reported, such as an address or an adapter still to come.
export function OtherEntries({
  others,
  editable,
  onRemove,
}: {
  others: string[];
  editable: boolean;
  onRemove: (entry: string) => void;
}) {
  const { t } = useLingui();

  return (
    <div className="flex flex-col gap-1.5">
      <span className="type-label text-ink">
        <Trans>Other entries</Trans>
      </span>
      <ul aria-label={t`Other entries`} className="flex flex-col">
        {others.map((entry) => (
          <li
            key={entry}
            className="flex items-center gap-3 border-b border-line-soft py-1.5 last:border-b-0"
          >
            <span className="min-w-0 flex-1 type-data break-words text-ink">{entry}</span>
            {editable ? (
              <Button
                size="sm"
                variant="quiet"
                aria-label={t`Remove ${entry}`}
                onPress={() => {
                  onRemove(entry);
                }}
              >
                <Trans>Remove</Trans>
              </Button>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  );
}
