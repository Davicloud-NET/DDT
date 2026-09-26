// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconCheck, IconMinus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  CheckboxButton,
  CheckboxField,
  SwitchButton,
  SwitchField,
  type CheckboxFieldProps,
  type SwitchFieldProps,
} from "react-aria-components";

import { cx } from "./cx";

const labelClass =
  "group flex cursor-pointer items-center gap-2.5 type-body text-ink outline-none disabled:cursor-not-allowed disabled:opacity-50";

const focusRing =
  "group-focus-visible:outline-2 group-focus-visible:outline-offset-2 group-focus-visible:outline-focus";

export interface CheckboxProps extends Omit<CheckboxFieldProps, "className" | "children"> {
  children?: ReactNode;
  className?: string | undefined;
}

// A square box: ink when ticked, an outline when not. Also the selection box of table rows (slot="selection").
export function Checkbox({ children, className, ...props }: CheckboxProps) {
  return (
    <CheckboxField {...props} className={cx("flex", className)}>
      <CheckboxButton className={labelClass}>
        {({ isSelected, isIndeterminate }) => (
          <>
            <span
              aria-hidden="true"
              className={cx(
                "flex size-4 shrink-0 items-center justify-center rounded-tag transition-colors",
                focusRing,
                isSelected || isIndeterminate
                  ? "bg-key-primary text-on-key-primary"
                  : "bg-field shadow-[inset_0_0_0_1px_var(--color-control)]",
              )}
            >
              {isIndeterminate ? (
                <IconMinus size={12} stroke={3} />
              ) : isSelected ? (
                <IconCheck size={12} stroke={3} />
              ) : null}
            </span>
            {children}
          </>
        )}
      </CheckboxButton>
    </CheckboxField>
  );
}

export interface SwitchProps extends Omit<SwitchFieldProps, "className" | "children"> {
  children: ReactNode;
  className?: string | undefined;
}

// For settings that take effect at once. A form that is saved with a button uses Checkbox instead.
export function Switch({ children, className, ...props }: SwitchProps) {
  return (
    <SwitchField {...props} className={cx("flex", className)}>
      <SwitchButton className={labelClass}>
        {({ isSelected }) => (
          <>
            <span
              aria-hidden="true"
              className={cx(
                "relative h-5 w-9 shrink-0 rounded-key transition-colors",
                focusRing,
                isSelected
                  ? "bg-key-primary"
                  : "bg-well shadow-[inset_0_0_0_1px_var(--color-control)]",
              )}
            >
              <span
                className={cx(
                  "absolute top-0.5 size-4 rounded-tag transition-[left,background-color]",
                  isSelected ? "left-4.5 bg-on-key-primary" : "left-0.5 bg-control",
                )}
              />
            </span>
            {children}
          </>
        )}
      </SwitchButton>
    </SwitchField>
  );
}
