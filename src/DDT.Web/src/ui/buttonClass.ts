// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "./cx";

// Primary is the single next step on a page. Danger is an outline, because a filled red button would read as
// the safe choice. Destructive work asks first, in a ConfirmDialog.
export type ButtonVariant = "primary" | "secondary" | "quiet" | "danger";
export type ButtonSize = "md" | "sm";

const base =
  "inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 whitespace-nowrap rounded-key type-label key-motion outline-none " +
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus " +
  "disabled:cursor-not-allowed disabled:opacity-45";

const variants: Record<ButtonVariant, string> = {
  primary:
    "bg-key-primary text-on-key-primary hover:bg-key-primary-hover pressed:bg-key-primary-pressed",
  secondary:
    "bg-key-secondary text-ink shadow-[inset_0_0_0_1px_var(--color-control)] hover:bg-key-secondary-hover pressed:bg-key-secondary-pressed",
  quiet: "text-ink-2 hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed",
  danger:
    "text-fail-text shadow-[inset_0_0_0_1.5px_var(--color-fail-text)] hover:bg-hover pressed:bg-key-quiet-pressed",
};

const sizes: Record<ButtonSize, string> = {
  md: "h-9 px-4",
  sm: "h-7.5 px-3",
};

export function buttonClass(
  variant: ButtonVariant = "secondary",
  size: ButtonSize = "md",
  className?: string,
): string {
  return cx(base, variants[variant], sizes[size], className);
}
