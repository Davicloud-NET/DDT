// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { formatBytes } from "@/lib/format";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";

import { logFilesQuery, logFileUrl } from "../logFiles";

// The log files of a server that runs as a Windows service, to download. Nothing where the server has none: in a
// container its log is the container's.
export function LogFiles() {
  const files = useQuery(logFilesQuery);
  const now = useNow(60_000);

  if (files.data === undefined || files.data.length === 0) {
    return null;
  }

  return (
    <Panel title={<Trans>Log files</Trans>}>
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          As a Windows service the server writes its log into the logs folder of its store, a file a
          day, and keeps the newest 20. The levels above decide what goes into them.
        </Trans>
      </p>
      <Facts
        items={files.data.map((file) => ({
          label: (
            <span title={fullTime(file.writtenUtc)}>
              {formatBytes(file.size)}, {relativeTime(file.writtenUtc, now)}
            </span>
          ),
          value: (
            <a
              href={logFileUrl(file.name)}
              download={file.name}
              className="text-ink underline underline-offset-3 hover:text-run-text"
            >
              {file.name}
            </a>
          ),
          mono: true,
        }))}
      />
    </Panel>
  );
}
