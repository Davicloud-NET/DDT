// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { EmptyState } from "@/ui/EmptyState";
import { Panel } from "@/ui/Panel";

// What the page shows without rules, and who adds them.
export function NoRules({ canEdit }: { canEdit: boolean }) {
  return (
    <Panel>
      <EmptyState title={<Trans>No rules yet</Trans>}>
        {canEdit ? (
          <Trans>
            Without rules an operator chooses the sequence of each machine. Add a rule to choose
            one, set values or give machine roles by what a machine is, such as its model or its
            network.
          </Trans>
        ) : (
          <Trans>
            Without rules an operator chooses the sequence of each machine. An administrator adds
            rules here.
          </Trans>
        )}
      </EmptyState>
    </Panel>
  );
}
