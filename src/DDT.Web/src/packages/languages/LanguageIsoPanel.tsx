// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { importSourcesQuery } from "@/images/import/imports";
import { ImportProgress } from "@/images/import/ImportProgress";
import { formatBytes } from "@/lib/format";
import { Button } from "@/ui/Button";
import { Panel } from "@/ui/Panel";

// The ISO files in the server's import folders, to take languages from. The import runs on the server, and its
// progress shows here.
export function LanguageIsoPanel({ onChoose }: { onChoose: (path: string) => void }) {
  const sources = useQuery(importSourcesQuery);

  if (sources.data === undefined) {
    return null;
  }

  const { folders, files, job } = sources.data;
  const isos = files.filter((file) => file.name.toLowerCase().endsWith(".iso"));
  const running = job?.state === "Running";
  const own = folders.find((folder) => folder.default)?.path ?? "";

  return (
    <Panel
      title={<Trans>Languages from the ISO</Trans>}
      actions={
        <Button size="sm" variant="quiet" onPress={() => void sources.refetch()}>
          <Trans>Look again</Trans>
        </Button>
      }
    >
      {job === null ? null : <ImportProgress job={job} />}

      {isos.length === 0 ? (
        <p className="max-w-[80ch] text-ink-2">
          <Trans>
            Upload the Languages and Optional Features ISO above, or copy it into{" "}
            <span className="type-data">{own}</span> on the server.
          </Trans>
        </p>
      ) : (
        <ul className="flex flex-col">
          {isos.map((file) => (
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
                isDisabled={running}
                onPress={() => {
                  onChoose(file.path);
                }}
              >
                <Trans>Choose languages</Trans>
              </Button>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
