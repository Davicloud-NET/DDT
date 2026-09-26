// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import {
  Tooltip as AriaTooltip,
  TooltipTrigger,
  type TooltipProps as AriaTooltipProps,
} from "react-aria-components";

// A short explanation on hover and keyboard focus, never the only place something important is said. It is ink on
// the page, inverted from the surface, so it reads in both themes.
export function Tooltip({
  children,
  content,
  placement = "top",
}: {
  // The element it explains; it must be focusable, such as a button.
  children: ReactNode;
  content: ReactNode;
  placement?: AriaTooltipProps["placement"];
}) {
  return (
    <TooltipTrigger delay={500} closeDelay={100}>
      {children}
      <AriaTooltip
        placement={placement}
        offset={6}
        className="max-w-72 rounded-key bg-ink px-2.5 py-1.5 type-small text-inverse entering:animate-overlay-in"
      >
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  );
}
