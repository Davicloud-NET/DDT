// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import type { ReactNode, RefObject } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { buttonClass, type ButtonVariant } from "@/ui/buttonClass";
import { NodeGlyph } from "@/ui/FlowNode";
import { Menu, MenuItem, MenuSection } from "@/ui/Menu";

import type { StepKind } from "../sequences";
import { addLabel, flowKinds, workKinds } from "./nodeKinds";

// The menu of kinds to add, for any key that opens it: in a MenuTrigger, or from triggerRef while isOpen.
export function NodeKindsMenu({
  onAdd,
  placement = "bottom start",
  triggerRef,
  isOpen,
  onOpenChange,
}: {
  onAdd: (kind: StepKind) => void;
  placement?: "bottom start" | "bottom end";
  triggerRef?: RefObject<Element | null>;
  isOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
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

// A key that opens the kinds to add. fullLabel says the key with its place for screen readers, such as "Add a step
// after Apply image", and starts with the words the key shows; the menu is named by its key.
export function AddNodeMenu({
  label,
  fullLabel,
  onAdd,
  variant = "secondary",
  isDisabled = false,
}: {
  label: ReactNode;
  fullLabel: string;
  onAdd: (kind: StepKind) => void;
  variant?: ButtonVariant;
  isDisabled?: boolean;
}) {
  return (
    <MenuTrigger>
      <AriaButton
        aria-label={fullLabel}
        isDisabled={isDisabled}
        className={buttonClass(variant, "sm")}
      >
        <IconPlus aria-hidden="true" size={16} stroke={2} />
        {label}
      </AriaButton>
      <NodeKindsMenu onAdd={onAdd} />
    </MenuTrigger>
  );
}
