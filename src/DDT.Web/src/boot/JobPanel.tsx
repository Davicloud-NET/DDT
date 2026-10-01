// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { relativeTime } from "@/lib/relativeTime";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { ProgressBar } from "@/ui/ProgressBar";
import { StateTag } from "@/ui/StateTag";

import { bootImageJobQuery, type BootImageJob } from "./bootImageJob";
import { JobOutput } from "./JobOutput";

// The build or ADK install that runs, or the last one, with what it writes. The hub pushes the output as it comes.
export function JobPanel({ job, now }: { job: BootImageJob; now: number }) {
  const live = useLiveStatus();
  const log = useQuery({ ...bootImageJobQuery, ...liveListOptions(live) });
  const lines = log.data?.job.startedUtc === job.startedUtc ? log.data.lines : [];
  const build = job.kind === "Build";
  const when = relativeTime(job.startedUtc, now);
  const startedBy = job.startedBy;

  return (
    <Panel
      title={build ? <Trans>Build</Trans> : <Trans>Install of the Windows ADK</Trans>}
      actions={<JobState job={job} />}
    >
      <p className="type-small text-muted">
        <Trans>
          Started {when} by {startedBy}
        </Trans>
      </p>
      {job.state === "Running" ? (
        <ProgressBar
          label={
            build ? (
              <Trans>Building the boot image, which takes several minutes</Trans>
            ) : (
              <Trans>Downloading and installing, which takes a few minutes</Trans>
            )
          }
        />
      ) : null}
      {job.problem === null ? null : <Notice tone="fail">{job.problem}</Notice>}
      <JobOutput
        lines={lines}
        missing={job.lines - lines.length}
        isRunning={job.state === "Running"}
      />
    </Panel>
  );
}

function JobState({ job }: { job: BootImageJob }) {
  switch (job.state) {
    case "Running":
      return (
        <StateTag tone="run">
          <Trans>Running</Trans>
        </StateTag>
      );
    case "Succeeded":
      return (
        <StateTag tone="ok">
          <Trans>Done</Trans>
        </StateTag>
      );
    case "Failed":
      return (
        <StateTag tone="fail">
          <Trans>Failed</Trans>
        </StateTag>
      );
  }
}
