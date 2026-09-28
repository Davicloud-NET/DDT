// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { RefObject } from "react";

import { Menu, MenuItem, MenuSection } from "@/ui/Menu";
import { NodeGlyph } from "@/ui/NodeGlyph";

import type { StepKind } from "../sequences";
import { addLabel, flowKinds, workKinds } from "./nodeKinds";

interface NodeKindsMenuProps {
  onAdd: (kind: StepKind) => void;
  placement?: "bottom start" | "bottom end";
  triggerRef?: RefObject<Element | null>;
  isOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
}

// The menu of kinds to add, for any key that opens it: in a MenuTrigger, or from triggerRef while isOpen.
export function NodeKindsMenu({
  onAdd,
  placement = "bottom start",
  triggerRef,
  isOpen,
  onOpenChange,
}: NodeKindsMenuProps) {
  return (
    <Menu
      placement={placement}
      {...(triggerRef === undefined ? {} : { triggerRef })}
      {...(isOpen === undefined ? {} : { isOpen })}
      {...(onOpenChange === undefined ? {} : { onOpenChange })}
      onAction={(key) => {
        onAdd(String(key) as StepKind);
      }}
    >
      <MenuSection title={<Trans>Flow</Trans>}>
        {flowKinds.map((kind) => (
          <MenuItem key={kind} id={kind} textValue={addLabel(kind)}>
            <NodeGlyph kind={kind} className="text-ink-2" />
            {addLabel(kind)}
          </MenuItem>
        ))}
      </MenuSection>
      <MenuSection title={<Trans>Steps</Trans>}>
        {workKinds.map((kind) => (
          <MenuItem key={kind} id={kind} textValue={addLabel(kind)}>
            <NodeGlyph kind={kind} className="text-ink-2" />
            {addLabel(kind)}
          </MenuItem>
        ))}
      </MenuSection>
    </Menu>
  );
}
