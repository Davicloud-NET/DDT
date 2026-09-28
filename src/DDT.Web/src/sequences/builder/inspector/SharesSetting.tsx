// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import { useContext, useId } from "react";

import { Button } from "@/ui/Button";

import { EditorLock } from "../../editorLock";
import type { Findings } from "../../problems";
import type { ShareConnection } from "../../sequences";
import { useBuilder } from "../builderData";
import { firstShareAccount, MAX_SHARES } from "./shares";
import { ShareRow } from "./ShareRow";

// The shares DDT connects before the step runs and disconnects after it, each with its account.
export function SharesSetting({
  shares,
  findings,
  onChange,
}: {
  shares: readonly ShareConnection[];
  findings: Findings;
  onChange: (shares: ShareConnection[], chosen: boolean) => void;
}) {
  const locked = useContext(EditorLock);
  const titleId = useId();
  const { accounts, inputs } = useBuilder();
  const first = firstShareAccount(accounts, inputs);

  return (
    <section aria-labelledby={titleId} data-field="shares" className="flex flex-col gap-2.5">
      <h3 id={titleId} className="type-label text-ink">
        <Trans>Network shares</Trans>
      </h3>
      <p className="type-small text-muted">
        {shares.length === 0 ? (
          <Trans>None. DDT connects the shares listed here before the step runs.</Trans>
        ) : (
          <Trans>
            DDT connects these shares with their accounts before the step runs and disconnects them
            after it. A share's host comes only from values fixed when the run starts.
          </Trans>
        )}
      </p>
      {shares.map((share, index) => (
        <ShareRow
          key={index}
          share={share}
          index={index}
          findings={findings}
          locked={locked}
          onSet={(changed, chosen) => {
            onChange(
              shares.map((other, at) => (at === index ? changed : other)),
              chosen,
            );
          }}
          onRemove={() => {
            onChange(
              shares.filter((_, at) => at !== index),
              true,
            );
          }}
        />
      ))}
      {locked ? null : (
        <div>
          <Button
            size="sm"
            variant="quiet"
            isDisabled={shares.length >= MAX_SHARES}
            onPress={() => {
              onChange([...shares, { path: "", account: first }], true);
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add a share</Trans>
          </Button>
        </div>
      )}
    </section>
  );
}
