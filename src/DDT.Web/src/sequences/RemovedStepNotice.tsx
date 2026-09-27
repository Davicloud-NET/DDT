// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useEffect, useEffectEvent } from "react";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

import type { SequenceStep } from "./sequences";
import { stepKindLabel } from "./steps";

// How long a removed step can be brought back from here.
const UNDO_MS = 10_000;

// Removing a step asks nothing; this brings it back for a few seconds. The parent keys it by the step, so each
// removal gets its own time. The focus comes here, as the removed step had it.
export function RemovedStepNotice({
  step,
  onUndo,
  onDismiss,
}: {
  step: SequenceStep;
  onUndo: () => void;
  onDismiss: () => void;
}) {
  const dismiss = useEffectEvent(() => {
    onDismiss();
  });

  useEffect(() => {
    const timer = window.setTimeout(() => {
      dismiss();
    }, UNDO_MS);

    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  const name = step.name;
  const kind = stepKindLabel(step.kind);

  return (
    <Notice
      actions={
        <Button size="sm" autoFocus onPress={onUndo}>
          <Trans>Undo</Trans>
        </Button>
      }
    >
      <Trans>
        Removed {name} ({kind}). You can bring it back for 10 seconds.
      </Trans>
    </Notice>
  );
}
