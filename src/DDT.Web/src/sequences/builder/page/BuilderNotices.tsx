// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { Notice } from "@/ui/Notice";

import { SequenceConflict } from "../../SequenceConflict";
import type { SequenceEditorState } from "../../useSequenceEditor";

// Why the flow cannot be changed, and another administrator's save that conflicts with this page's.
export function BuilderNotices({
  editor,
  readOnly,
}: {
  editor: SequenceEditorState;
  readOnly: boolean;
}) {
  return (
    <>
      {readOnly ? (
        <Notice>
          <Trans>Only administrators change task sequences. You can look at this one.</Trans>
        </Notice>
      ) : null}

      {editor.deleted ? (
        <Notice tone="fail" title={<Trans>This sequence was deleted</Trans>}>
          <Trans>
            Someone deleted it while it was open here, so nothing more is saved.{" "}
            <Link to="/deployment/sequences" className="font-semibold underline">
              Go to the task sequences
            </Link>
            .
          </Trans>
        </Notice>
      ) : null}

      {editor.conflict !== null ? (
        <SequenceConflict
          theirs={editor.conflict.view}
          changes={editor.conflict.changes}
          onTakeTheirs={editor.takeTheirs}
          onKeepMine={editor.keepMine}
        />
      ) : null}
    </>
  );
}
