// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { Link as AriaLink, type LinkProps as AriaLinkProps } from "react-aria-components";

import { buttonClass, type ButtonSize, type ButtonVariant } from "./buttonClass";

export interface LinkButtonProps extends Omit<AriaLinkProps, "className" | "children"> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string | undefined;
  children: ReactNode;
}

// A link that looks like a button, for actions that go to another page.
export function LinkButton({ variant, size, className, ...props }: LinkButtonProps) {
  return <AriaLink {...props} className={buttonClass(variant, size, className)} />;
}
