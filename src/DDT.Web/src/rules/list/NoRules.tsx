// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { EmptyState } from "@/ui/EmptyState";
import { Panel } from "@/ui/Panel";

// What the page shows when there are no rules, and who can add them.
export function NoRules({ canEdit }: { canEdit: boolean }) {
  return (
    <Panel>
      <EmptyState title={<Trans>No rules yet</Trans>}>
        {canEdit ? null : <Trans>An administrator adds rules here.</Trans>}
      </EmptyState>
    </Panel>
  );
}
