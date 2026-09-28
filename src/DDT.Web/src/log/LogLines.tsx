// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

import { LogViewport } from "./LogViewport";
import type { LogView } from "./useLogView";

// The filtered lines, and while following is paused, a bar that counts the new ones and jumps to them.
export function LogLines({ view }: { view: LogView }) {
  return (
    <div className="relative">
      <LogViewport
        lines={view.lines}
        following={view.following}
        onFollowingChange={view.setFollowing}
        selectedId={view.selected?.id ?? null}
        onSelect={view.setSelected}
      />
      {view.all.length > 0 && view.lines.length === 0 ? (
        <p className="absolute inset-x-0 top-4 text-center type-small text-console-muted">
          <Trans>No loaded line matches the filter.</Trans>
        </p>
      ) : null}
      {!view.following ? (
        <PausedBar
          newLines={view.newLines}
          onJump={() => {
            view.setFollowing(true);
          }}
        />
      ) : null}
    </div>
  );
}

function PausedBar({ newLines, onJump }: { newLines: number; onJump: () => void }) {
  return (
    <div
      role="status"
      className="absolute right-3 bottom-3 flex items-center gap-3 rounded-key bg-raised px-3 py-1.5 type-small shadow-overlay"
    >
      <span>
        {newLines > 0 ? (
          plural(newLines, { one: "Paused, # new line", other: "Paused, # new lines" })
        ) : (
          <Trans>Paused</Trans>
        )}
      </span>
      <Button size="sm" variant="primary" onPress={onJump}>
        <Trans>Jump to the newest</Trans>
      </Button>
    </div>
  );
}
