// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

interface EntryMenuProps {
  name: string;
  onEdit: () => void;
  onDelete: () => void;
}

// The row menu of an application or a bundle.
export function EntryMenu({ name, onEdit, onDelete }: EntryMenuProps) {
  const { t } = useLingui();

  return (
    <RowActionsMenu
      label={t`Actions for ${name}`}
      className="size-8"
      onAction={(key) => {
        if (key === "edit") {
          onEdit();
        } else {
          onDelete();
        }
      }}
    >
      <MenuItem id="edit">
        <Trans>Edit</Trans>
      </MenuItem>
      <MenuItem id="delete" className="text-fail-text">
        <Trans>Delete</Trans>
      </MenuItem>
    </RowActionsMenu>
  );
}
