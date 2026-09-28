// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";

// The rules page's title, how the rules are checked, and the button that adds one.
export function RulesHeader({ canEdit, onAdd }: { canEdit: boolean; onAdd: () => void }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div className="flex max-w-155 flex-col gap-2.5">
        <h1 className="type-title text-ink">
          <Trans>Rules</Trans>
        </h1>
        <p className="text-ink-2">
          <Trans>
            Checked from the top. The first rule that chooses a sequence chooses it, and the first
            rule that sets a value sets it. A rule never approves a machine.
          </Trans>
        </p>
      </div>
      {canEdit ? (
        <Button variant="primary" onPress={onAdd}>
          <IconPlus aria-hidden="true" size={14} stroke={2} />
          <Trans>Add rule</Trans>
        </Button>
      ) : null}
    </div>
  );
}
