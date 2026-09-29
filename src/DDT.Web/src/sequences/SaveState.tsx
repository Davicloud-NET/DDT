// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import type { AutosaveState } from "@/lib/autosave";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";

import { clockTime } from "./sequenceView";

function describe(
  state: AutosaveState,
  savedElsewhere: { by: string | null; at: string } | null,
  now: number,
): string {
  switch (state.kind) {
    case "saved": {
      if (savedElsewhere !== null) {
        const by = savedElsewhere.by;
        const time = clockTime(savedElsewhere.at);

        return by === null
          ? t`Another administrator saved it at ${time}`
          : t`${by} saved it at ${time}`;
      }

      if (state.at === null) {
        return t`All changes saved`;
      }

      const time = clockTime(state.at);

      return t`All changes saved at ${time}`;
    }
    case "pending":
      return t`Unsaved changes`;
    case "saving":
      return t`Saving`;
    case "retrying": {
      const message = state.message;
      const seconds = Math.max(0, Math.ceil((state.nextAt - now) / 1000));

      return t`Not saved: ${message} Trying again in ${seconds} s.`;
    }
    case "refused":
    case "stopped": {
      const message = state.message;

      return t`Not saved: ${message}`;
    }
    case "conflict":
      return t`Not saved: another administrator saved this sequence`;
  }
}

// The save status, beside the sequence's name. Every change is saved as it's made, so this is the only place that
// says whether the save worked.
export function SaveState({
  state,
  savedElsewhere,
  onRetry,
  className,
}: {
  state: AutosaveState;
  savedElsewhere: { by: string | null; at: string } | null;
  onRetry: () => void;
  className?: string;
}) {
  // Counts down to the next attempt.
  const now = useNow(state.kind === "retrying" ? 1_000 : 60_000);
  const failing = ["retrying", "refused", "conflict", "stopped"].includes(state.kind);

  return (
    <div className={cx("flex flex-wrap items-center gap-x-3 gap-y-1", className)}>
      <p
        role="status"
        className={cx("type-small", failing ? "font-semibold text-fail-text" : "text-muted")}
      >
        {describe(state, savedElsewhere, now)}
      </p>
      {state.kind === "retrying" ? (
        <Button size="sm" variant="quiet" onPress={onRetry}>
          <Trans>Try now</Trans>
        </Button>
      ) : null}
    </div>
  );
}
