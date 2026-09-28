// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

interface SavedMeanwhileProps {
  title: ReactNode;
  onTakeTheirs: () => void;
  onKeepMine: () => void;
  isBusy: boolean;
}

// Someone saved the thing while it was being edited here. Nothing is saved until one copy is chosen: theirs, which
// throws the edits here away, or this one, saved over theirs.
export function SavedMeanwhile({ title, onTakeTheirs, onKeepMine, isBusy }: SavedMeanwhileProps) {
  return (
    <Notice
      tone="attention"
      title={title}
      actions={
        <>
          <Button size="sm" isDisabled={isBusy} onPress={onTakeTheirs}>
            <Trans>Use theirs</Trans>
          </Button>
          <Button size="sm" isDisabled={isBusy} onPress={onKeepMine}>
            <Trans>Keep mine</Trans>
          </Button>
        </>
      }
    >
      <Trans>Use theirs throws your changes away. Keep mine saves your version over theirs.</Trans>
    </Notice>
  );
}
