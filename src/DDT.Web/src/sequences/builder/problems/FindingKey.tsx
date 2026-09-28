// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";

import type { FindingTone } from "./problemList";

// A finding in the problems list, a key that takes the focus to its field where it has one.
export function FindingKey({
  message,
  tone,
  label,
  onGoTo,
}: {
  message: string;
  tone: FindingTone;
  label: string;
  onGoTo: (() => void) | null;
}) {
  const toned = cx("type-small", tone === "fail" ? "text-fail-text" : "text-attention-text");

  return onGoTo === null ? (
    <span className={cx("px-1.5 py-1", toned)}>{message}</span>
  ) : (
    <AriaButton
      aria-label={label}
      onPress={onGoTo}
      className={cx(
        "-mx-0 cursor-pointer rounded-key px-1.5 py-1 text-left motion-colors outline-none hover:bg-hover",
        "focus-visible:outline-2 focus-visible:outline-focus",
        toned,
      )}
    >
      {message}
    </AriaButton>
  );
}
