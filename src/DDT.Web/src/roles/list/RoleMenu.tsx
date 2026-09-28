// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

interface RoleMenuProps {
  name: string;
  onOpen: () => void;
  onDelete: () => void;
}

export function RoleMenu({ name, onOpen, onDelete }: RoleMenuProps) {
  const { t } = useLingui();
  const label = t`Actions for ${name}`;

  return (
    <RowActionsMenu
      label={label}
      className="size-8"
      onAction={(key) => {
        if (key === "edit") {
          onOpen();
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
