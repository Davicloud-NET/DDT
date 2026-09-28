// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { Keyboard, Text } from "react-aria-components";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

interface RuleMenuProps {
  name: string;
  first: boolean;
  last: boolean;
  onOpen: () => void;
  onMove: (offset: number) => void;
  onDelete: () => void;
}

// A rule's actions, with the moves that Alt and the arrow keys also make.
export function RuleMenu({ name, first, last, onOpen, onMove, onDelete }: RuleMenuProps) {
  const { t } = useLingui();
  const label = t`Actions for ${name}`;

  return (
    <RowActionsMenu
      label={label}
      className="col-start-4 row-span-2 row-start-1 size-8 sm:col-start-5 sm:row-span-1"
      disabledKeys={[...(first ? ["up"] : []), ...(last ? ["down"] : [])]}
      onAction={(key) => {
        switch (key) {
          case "edit":
            onOpen();
            break;
          case "up":
            onMove(-1);
            break;
          case "down":
            onMove(1);
            break;
          case "delete":
            onDelete();
            break;
        }
      }}
    >
      <MenuItem id="edit">
        <Trans>Change</Trans>
      </MenuItem>
      <MenuItem id="up" textValue={t`Move up`}>
        <Text slot="label" className="flex-1">
          <Trans>Move up</Trans>
        </Text>
        <Keyboard className="type-small text-muted">Alt+↑</Keyboard>
      </MenuItem>
      <MenuItem id="down" textValue={t`Move down`}>
        <Text slot="label" className="flex-1">
          <Trans>Move down</Trans>
        </Text>
        <Keyboard className="type-small text-muted">Alt+↓</Keyboard>
      </MenuItem>
      <MenuItem id="delete" className="text-fail-text">
        <Trans>Delete</Trans>
      </MenuItem>
    </RowActionsMenu>
  );
}
