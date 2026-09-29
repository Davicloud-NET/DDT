// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconPlus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { buttonClass, type ButtonVariant } from "@/ui/buttonClass";

import type { StepKind } from "../sequences";
import { NodeKindsMenu } from "./NodeKindsMenu";

interface AddNodeMenuProps {
  label: ReactNode;
  fullLabel: string;
  onAdd: (kind: StepKind) => void;
  variant?: ButtonVariant;
  isDisabled?: boolean;
}

// A button that opens the kinds to add. fullLabel is the button's screen reader label with its place, such as "Add a
// step after Apply image". It starts with the words the button shows. The menu is named after its button.
export function AddNodeMenu({
  label,
  fullLabel,
  onAdd,
  variant = "secondary",
  isDisabled = false,
}: AddNodeMenuProps) {
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
