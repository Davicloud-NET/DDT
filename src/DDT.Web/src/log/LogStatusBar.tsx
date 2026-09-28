// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { number } from "@/lib/format";
import { Button } from "@/ui/Button";

import { STORED_LINES_PER_MACHINE } from "./log";
import { MAX_BUFFERED_LINES } from "./logBuffer";
import { LOG_POLL_MS } from "./useMachineLog";
import type { LogView } from "./useLogView";

// Older lines to load or the limits that stop them, and how new lines arrive while the live connection is down.
export function LogStatusBar({ view }: { view: LogView }) {
  const log = view.log;
  const full = view.all.length >= MAX_BUFFERED_LINES;
  const bufferLimit = number(MAX_BUFFERED_LINES);
  const storedLimit = number(STORED_LINES_PER_MACHINE);
  const pollSeconds = LOG_POLL_MS / 1000;

  return (
    <div className="flex flex-wrap items-center gap-3 type-small text-muted">
      {log.buffer.hasOlder && !full ? (
        <Button size="sm" isDisabled={log.loadingOlder} onPress={log.loadOlder}>
          {log.loadingOlder ? <Trans>Loading older lines</Trans> : <Trans>Load older lines</Trans>}
        </Button>
      ) : null}
      {log.buffer.hasOlder && full ? (
        <span>
          <Trans>This page holds at most {bufferLimit} lines. Older ones stay on the server.</Trans>
        </span>
      ) : null}
      {log.loaded && !log.buffer.hasOlder && view.all.length > 0 ? (
        <span>
          <Trans>
            Start of the stored log. The server keeps the newest {storedLimit} lines of each
            machine.
          </Trans>
        </span>
      ) : null}
      <span className="flex-1" />
      <span>
        {log.polling ? (
          <Trans>Reading every {pollSeconds} s, because the live connection is down.</Trans>
        ) : log.status === "reconnecting" ? (
          <Trans>Reconnecting. Lines sent meanwhile are read once the connection is back.</Trans>
        ) : log.status === "live" ? null : (
          <Trans>The live connection is down. New lines appear once it is back.</Trans>
        )}
      </span>
    </div>
  );
}
