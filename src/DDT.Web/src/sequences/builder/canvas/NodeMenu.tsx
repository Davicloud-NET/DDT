// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import type { RefObject } from "react";

import { Menu, MenuItem, MenuSeparator } from "@/ui/Menu";

import { nodeTitle } from "../../flow/flowLabels";
import type { SequenceStep } from "../../sequences";
import { isContainer } from "../../steps";
import { nodeActionOf, type NodeAction } from "./nodeAction";

interface NodeMenuProps {
  node: SequenceStep;
  triggerRef: RefObject<HTMLElement | null>;
  locked: boolean;
  collapsed: boolean;
  onClose: () => void;
  onAction: (action: NodeAction, id: string) => void;
}

// A node's menu, from its context menu or Shift+F10; a viewer only copies and collapses.
export function NodeMenu({
  node,
  triggerRef,
  locked,
  collapsed,
  onClose,
  onAction,
}: NodeMenuProps) {
  const { t } = useLingui();
  const menuName = nodeTitle(node);

  return (
    <Menu
      aria-label={t`Actions for ${menuName}`}
      triggerRef={triggerRef}
      isOpen
      placement="bottom start"
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      onAction={(key) => {
        const id = node.id;
        const action = nodeActionOf(String(key));

        onClose();

        if (action !== null) {
          onAction(action, id);
        }
      }}
    >
      {locked ? null : (
        <>
          <MenuItem id="addAfter">
            <Trans>Add a step after it</Trans>
          </MenuItem>
          <MenuSeparator />
          <MenuItem id="wrapGroup">
            <Trans>Wrap in a group</Trans>
          </MenuItem>
          <MenuItem id="wrapIf">
            <Trans>Wrap in an If</Trans>
          </MenuItem>
          <MenuItem id="wrapRepeat">
            <Trans>Wrap in a Repeat</Trans>
          </MenuItem>
          {isContainer(node) ? (
            <MenuItem id="unwrap">
              <Trans>Unwrap</Trans>
            </MenuItem>
          ) : null}
        </>
      )}
      {isContainer(node) ? (
        <MenuItem id="collapse">
          {collapsed ? <Trans>Expand</Trans> : <Trans>Collapse</Trans>}
        </MenuItem>
      ) : null}
      <MenuSeparator />
      <MenuItem id="copy">
        <Trans>Copy</Trans>
      </MenuItem>
      {locked ? null : (
        <>
          <MenuItem id="cut">
            <Trans>Cut</Trans>
          </MenuItem>
          <MenuItem id="duplicate">
            <Trans>Duplicate</Trans>
          </MenuItem>
          <MenuItem id="remove" className="text-fail-text">
            <Trans>Remove</Trans>
          </MenuItem>
        </>
      )}
    </Menu>
  );
}
