// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import type { Findings } from "../../problems";
import type { ShareConnection } from "../../sequences";
import { AccountSetting } from "../AccountSetting";
import { TemplateField } from "../TemplateField";

const iconKey =
  "flex size-8 shrink-0 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus";

// One share of a step: its path and the account it is connected with.
export function ShareRow({
  share,
  index,
  findings,
  locked,
  onSet,
  onRemove,
}: {
  share: ShareConnection;
  index: number;
  findings: Findings;
  locked: boolean;
  onSet: (share: ShareConnection, chosen: boolean) => void;
  onRemove: () => void;
}) {
  const { t } = useLingui();
  const number = index + 1;

  return (
    <div className="flex flex-col gap-3 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
      <div className="flex items-start gap-1.5">
        <TemplateField
          label={t`Share ${number}`}
          field={`shares[${String(index)}].path`}
          findings={findings}
          className="min-w-0 flex-1"
          placeholder="\\fs01.corp.example\deploy"
          howTo={false}
          value={share.path}
          onChange={(path) => {
            onSet({ ...share, path }, false);
          }}
        />
        {locked ? null : (
          <AriaButton
            aria-label={t`Remove share ${number}`}
            className={`${iconKey} mt-6.5`}
            onPress={onRemove}
          >
            <IconX size={14} stroke={2} />
          </AriaButton>
        )}
      </div>
      <AccountSetting
        label={t`Account for share ${number}`}
        field={`shares[${String(index)}].account`}
        findings={findings}
        use="share"
        value={share.account}
        onChange={(account) => {
          if (account !== null) {
            onSet({ ...share, account }, true);
          }
        }}
      />
    </div>
  );
}
