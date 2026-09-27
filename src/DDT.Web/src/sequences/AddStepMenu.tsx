// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { buttonClass, type ButtonVariant } from "@/ui/buttonClass";
import { Menu, MenuItem, MenuSection } from "@/ui/Menu";

import type { StepKind } from "./sequences";
import { stepKindLabel } from "./steps";

// The kinds of step, grouped by the sequences that use them.
const groups: { title: ReactNode; kinds: StepKind[] }[] = [
  {
    title: <Trans>Install Windows</Trans>,
    kinds: ["partition", "applyImage", "injectDrivers", "writeUnattend", "joinDomain"],
  },
  { title: <Trans>Write a disk image</Trans>, kinds: ["writeRawImage", "writeCloudInitSeed"] },
  { title: <Trans>Any sequence</Trans>, kinds: ["runScript", "reboot"] },
];

// A key that opens the kinds of step to add. fullLabel says the key with its place for screen readers, such as
// "Insert after Apply image", and starts with the words the key shows; the menu is named by its key.
export function AddStepMenu({
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
      <Menu
        placement="bottom start"
        onAction={(key) => {
          onAdd(String(key) as StepKind);
        }}
      >
        {groups.map((group, index) => (
          <MenuSection key={index} title={group.title}>
            {group.kinds.map((kind) => (
              <MenuItem key={kind} id={kind}>
                {stepKindLabel(kind)}
              </MenuItem>
            ))}
          </MenuSection>
        ))}
      </Menu>
    </MenuTrigger>
  );
}
