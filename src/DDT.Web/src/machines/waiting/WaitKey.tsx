// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";

const keyClass =
  "inline-flex h-9.5 shrink-0 cursor-pointer items-center justify-center rounded-key bg-on-attention px-4 type-label font-bold text-attention key-motion outline-none " +
  "hover:bg-on-attention/85 pressed:bg-on-attention/75 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-on-attention disabled:cursor-not-allowed disabled:opacity-60";

// A key in the inverted colours of the notice's attention background, which no Button variant has.
export function WaitKey({
  isDisabled,
  onPress,
  children,
}: {
  isDisabled: boolean;
  onPress: () => void;
  children: ReactNode;
}) {
  return (
    <AriaButton className={cx(keyClass)} isDisabled={isDisabled} onPress={onPress}>
      {children}
    </AriaButton>
  );
}
