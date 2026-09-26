// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import {
  Button as AriaButton,
  Link as AriaLink,
  type ButtonProps as AriaButtonProps,
  type LinkProps as AriaLinkProps,
} from "react-aria-components";

import { buttonClass, type ButtonSize, type ButtonVariant } from "./buttonClass";

export interface ButtonProps extends Omit<AriaButtonProps, "className" | "children"> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string | undefined;
  children: ReactNode;
}

export function Button({ variant, size, className, ...props }: ButtonProps) {
  return <AriaButton {...props} className={buttonClass(variant, size, className)} />;
}

export interface LinkButtonProps extends Omit<AriaLinkProps, "className" | "children"> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string | undefined;
  children: ReactNode;
}

// A link that looks like a key, for actions that go to another page.
export function LinkButton({ variant, size, className, ...props }: LinkButtonProps) {
  return <AriaLink {...props} className={buttonClass(variant, size, className)} />;
}
