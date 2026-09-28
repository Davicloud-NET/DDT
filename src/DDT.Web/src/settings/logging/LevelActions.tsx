// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";

export function LevelActions({
  isDefault,
  onAdd,
  onRestoreDefaults,
}: {
  isDefault: boolean;
  onAdd: () => void;
  onRestoreDefaults: () => void;
}) {
  return (
    <span className="flex flex-wrap items-center gap-3">
      <Button size="sm" onPress={onAdd}>
        <IconPlus size={14} stroke={2} aria-hidden="true" />
        <Trans>Add a category</Trans>
      </Button>
      <Button size="sm" variant="quiet" isDisabled={isDefault} onPress={onRestoreDefaults}>
        <Trans>Use the defaults</Trans>
      </Button>
      <span className="type-small text-muted">
        <Trans>
          The defaults: Default at Information, and Microsoft.AspNetCore and the two Entity
          Framework Core categories at Warning.
        </Trans>
      </span>
    </span>
  );
}
