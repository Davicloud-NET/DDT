// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { SwitchButton, SwitchField, type SwitchFieldProps } from "react-aria-components";

import { cx } from "./cx";
import { toggleFocusRing, toggleLabelClass } from "./toggleClass";

export interface SwitchProps extends Omit<SwitchFieldProps, "className" | "children"> {
  children: ReactNode;
  className?: string | undefined;
}

// For settings that take effect at once. A form that is saved with a button uses Checkbox instead.
export function Switch({ children, className, ...props }: SwitchProps) {
  return (
    <SwitchField {...props} className={cx("flex", className)}>
      <SwitchButton className={toggleLabelClass}>
        {({ isSelected }) => (
          <>
            <span
              aria-hidden="true"
              className={cx(
                "relative h-5 w-9 shrink-0 rounded-key motion-colors",
                toggleFocusRing,
                isSelected
                  ? "bg-key-primary"
                  : "bg-well shadow-[inset_0_0_0_1px_var(--color-control)]",
              )}
            >
              {/* The thumb slides across, as a switch's lever does. */}
              <span
                className={cx(
                  "absolute top-0.5 left-0.5 size-4 rounded-tag transition-[translate,background-color] duration-(--duration-fast) ease-standard",
                  isSelected ? "translate-x-4 bg-on-key-primary" : "bg-control",
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
