// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconArrowBackUp, IconArrowForwardUp } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { buttonClass } from "@/ui/buttonClass";
import { Tooltip } from "@/ui/Tooltip";

import type { SequenceEditorState } from "../../useSequenceEditor";

// The Undo and Redo buttons; Ctrl+Z and Ctrl+Y work anywhere but in a text field, which keeps its own undo.
export function UndoRedo({ editor }: { editor: SequenceEditorState }) {
  const { t } = useLingui();

  return (
    <div className="flex gap-1">
      <Tooltip content={<Trans>Undo, Ctrl+Z</Trans>}>
        <AriaButton
          aria-label={t`Undo`}
          aria-keyshortcuts="Control+Z"
          isDisabled={!editor.canUndo}
          className={buttonClass("secondary", "md", "w-9 px-0")}
          onPress={editor.undo}
        >
          <IconArrowBackUp aria-hidden="true" size={18} stroke={2} />
        </AriaButton>
      </Tooltip>
      <Tooltip content={<Trans>Redo, Ctrl+Y</Trans>}>
        <AriaButton
          aria-label={t`Redo`}
          aria-keyshortcuts="Control+Y"
          isDisabled={!editor.canRedo}
          className={buttonClass("secondary", "md", "w-9 px-0")}
          onPress={editor.redo}
        >
          <IconArrowForwardUp aria-hidden="true" size={18} stroke={2} />
        </AriaButton>
      </Tooltip>
    </div>
  );
}
