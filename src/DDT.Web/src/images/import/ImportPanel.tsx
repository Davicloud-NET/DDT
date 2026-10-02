// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { formatBytes } from "@/lib/format";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import { importFiles, importSourcesQuery } from "./imports";
import { ImportProgress } from "./ImportProgress";
import { MdtShareDialog } from "./MdtShareDialog";

// Images that are on the server already: a WIM, ESD or ISO in one of its import folders, and an MDT deployment share.
// Importing them spares the upload, the slowest part of a first deployment.
export function ImportPanel() {
  const sources = useQuery(importSourcesQuery);
  const [share, setShare] = useState<string | null>(null);
  const start = useMutation({ mutationFn: (path: string) => importFiles([path]) });

  // A server that offers no import, such as an older one, shows nothing here
  if (sources.data === undefined) {
    return null;
  }

  const { folders, files, shares, job } = sources.data;
  const running = job?.state === "Running";
  const own = folders.find((folder) => folder.default)?.path ?? "";

  return (
    <Panel
      title={<Trans>Import from the server</Trans>}
      actions={
        <Button size="sm" variant="quiet" onPress={() => void sources.refetch()}>
          <Trans>Look again</Trans>
        </Button>
      }
    >
      {job === null ? null : <ImportProgress job={job} />}
      {start.isError ? <Notice tone="fail">{start.error.message}</Notice> : null}

      {files.length === 0 && shares.length === 0 ? (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            A WIM, ESD or ISO copied into <span className="type-data">{own}</span> on the server
            shows here and is imported without an upload. Configuration names more folders, such as
            an MDT deployment share, in DDT:ImportFolders.
          </Trans>
        </p>
      ) : (
        <ul className="flex flex-col">
          {shares.map((path) => (
            <li
              key={path}
              className="flex items-center gap-3 border-b border-line-soft py-2.5 last:border-b-0"
            >
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="type-label text-ink">
                  <Trans>MDT deployment share</Trans>
                </span>
                <span className="truncate type-data text-muted">{path}</span>
              </span>
              <Button
                size="sm"
                isDisabled={running}
                onPress={() => {
                  setShare(path);
                }}
              >
                <Trans>Choose what to import</Trans>
              </Button>
            </li>
          ))}
          {files.map((file) => (
            <li
              key={file.path}
              className="flex items-center gap-3 border-b border-line-soft py-2.5 last:border-b-0"
            >
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate type-label text-ink">{file.name}</span>
                <span className="truncate type-data text-muted">{file.path}</span>
              </span>
              <span className="type-small text-muted">{formatBytes(file.sizeBytes)}</span>
              <Button
                size="sm"
                isDisabled={running || start.isPending}
                onPress={() => {
                  start.mutate(file.path);
                }}
              >
                <Trans>Import</Trans>
              </Button>
            </li>
          ))}
        </ul>
      )}

      <MdtShareDialog
        path={share}
        onClose={() => {
          setShare(null);
        }}
      />
    </Panel>
  );
}
