// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

import type { PackageSummary } from "./packages";

export function PackageMenu({
  item,
  onEdit,
  onDelete,
}: {
  item: PackageSummary;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const { t: translate } = useLingui();
  const name = item.name;

  return (
    <RowActionsMenu
      label={translate`Actions for ${name}`}
      onAction={(key) => {
        if (key === "edit") {
          onEdit();
        } else {
          onDelete();
        }
      }}
    >
      <MenuItem id="edit">
        <Trans>Change</Trans>
      </MenuItem>
      <MenuItem id="delete" className="text-fail-text">
        <Trans>Delete</Trans>
      </MenuItem>
    </RowActionsMenu>
  );
}
