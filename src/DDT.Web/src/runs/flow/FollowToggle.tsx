// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { ToggleButton } from "react-aria-components";

import { cx } from "@/ui/cx";

export function FollowToggle({
  isSelected,
  onChange,
}: {
  isSelected: boolean;
  onChange: (on: boolean) => void;
}) {
  return (
    <ToggleButton
      isSelected={isSelected}
      onChange={(on) => {
        onChange(on);
      }}
      className={cx(
        "flex h-full cursor-pointer items-center border-l border-line-soft px-3 font-semibold text-ink motion-colors outline-none",
        "hover:bg-hover pressed:bg-key-quiet-pressed selected:bg-selected",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
      )}
    >
      <Trans>Follow the run</Trans>
    </ToggleButton>
  );
}
