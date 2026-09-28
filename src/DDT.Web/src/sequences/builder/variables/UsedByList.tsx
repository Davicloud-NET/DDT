// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";

import { nodeTitle } from "../../flow/flowLabels";
import { findNode } from "../../flow/flowTree";
import type { SequenceDraft } from "../../sequenceDraft";

// The nodes that use a variable or an input. Each is a button that goes to the node.
export function UsedByList({
  users,
  draft,
  onGoToNode,
}: {
  users: readonly string[];
  draft: SequenceDraft;
  onGoToNode: (id: string) => void;
}) {
  const { t } = useLingui();

  return (
    <div className="flex flex-col gap-1">
      <span className="type-label text-ink">
        <Trans>Used by</Trans>
      </span>
      <ul className="flex flex-col">
        {users.map((id) => {
          const node = findNode(draft.steps, id);
          const title = node === undefined ? id : nodeTitle(node);

          return (
            <li key={id}>
              <AriaButton
                aria-label={t`Go to ${title}`}
                className="cursor-pointer rounded-key px-1 py-0.5 text-left type-small text-ink-2 underline outline-none hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
                onPress={() => {
                  onGoToNode(id);
                }}
              >
                {title}
              </AriaButton>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
