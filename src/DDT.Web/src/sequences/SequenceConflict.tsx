// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";

import type { SequenceView } from "./sequences";
import { clockTime } from "./sequenceView";

// Another administrator saved while this page had unsaved edits. Nothing is saved until one copy is chosen: theirs,
// which throws this page's edits away, or this page's, saved over theirs once confirmed.
export function SequenceConflict({
  theirs,
  changes,
  onTakeTheirs,
  onKeepMine,
}: {
  // Their copy, once it is read.
  theirs: SequenceView | null;
  // What they changed against the copy this page started from, such as "the name" or a step's name.
  changes: string[];
  onTakeTheirs: () => void;
  onKeepMine: () => void;
}) {
  const [confirming, setConfirming] = useState(false);

  const who = theirs?.updatedBy ?? null;
  const when = theirs === null ? null : clockTime(theirs.updatedUtc);
  const list = changes.join(", ");

  const title =
    who !== null && when !== null
      ? t`${who} saved this sequence at ${when} while you were editing.`
      : t`Another administrator saved this sequence while you were editing.`;
  const replaces =
    who !== null && when !== null
      ? t`Your version replaces the one ${who} saved at ${when}.`
      : t`Your version replaces the one saved by another administrator.`;

  return (
    <>
      <Notice
        tone="attention"
        title={title}
        actions={
          <>
            <Button size="sm" isDisabled={theirs === null} onPress={onTakeTheirs}>
              <Trans>Use theirs</Trans>
            </Button>
            <Button
              size="sm"
              isDisabled={theirs === null}
              onPress={() => {
                setConfirming(true);
              }}
            >
              <Trans>Keep mine</Trans>
            </Button>
          </>
        }
      >
        <Trans>Your changes since your last save are not saved.</Trans>{" "}
        {changes.length > 0 ? <Trans>They changed {list}.</Trans> : null}{" "}
        {theirs === null ? <Trans>Reading their version.</Trans> : null}{" "}
        <Trans>
          Use theirs throws your unsaved changes away. Keep mine saves your version over theirs.
        </Trans>
      </Notice>

      <ConfirmDialog
        isOpen={confirming}
        onOpenChange={setConfirming}
        title={<Trans>Save your version over theirs?</Trans>}
        confirmLabel={<Trans>Save my version</Trans>}
        onConfirm={() => {
          setConfirming(false);
          onKeepMine();
        }}
      >
        <p>
          {replaces} {changes.length > 0 ? <Trans>Their changes to {list} are lost.</Trans> : null}
        </p>
      </ConfirmDialog>
    </>
  );
}
