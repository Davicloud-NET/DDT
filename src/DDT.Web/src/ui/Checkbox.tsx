// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconCheck, IconMinus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { CheckboxButton, CheckboxField, type CheckboxFieldProps } from "react-aria-components";

import { cx } from "./cx";
import { toggleFocusRing, toggleLabelClass } from "./toggleClass";

export interface CheckboxProps extends Omit<CheckboxFieldProps, "className" | "children"> {
  children?: ReactNode;
  className?: string | undefined;
}

// A square box: ink when ticked, an outline when not. Also the selection box of table rows (slot="selection").
export function Checkbox({ children, className, ...props }: CheckboxProps) {
  return (
    <CheckboxField {...props} className={cx("flex", className)}>
      <CheckboxButton className={toggleLabelClass}>
        {({ isSelected, isIndeterminate }) => (
          <>
            <span
              aria-hidden="true"
              className={cx(
                "flex size-4 shrink-0 items-center justify-center rounded-tag motion-colors",
                toggleFocusRing,
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
