// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { percentOf } from "@/lib/format";
import { serverText } from "@/lib/serverText";
import { Notice } from "@/ui/Notice";
import { ProgressBar } from "@/ui/ProgressBar";

import type { ImportResult, ImportStatus } from "./imports";

// How far the running import is, and what became of each file of the last one.
export function ImportProgress({ job }: { job: ImportStatus }) {
  const item = job.item ?? "";
  const position = job.results.length + 1;
  const count = job.items;
  const refused = job.results.filter((result) => result.outcome === "Refused");

  return (
    <>
      {job.state === "Running" ? (
        <ProgressBar
          label={
            <Trans>
              Reading {item}, {position} of {count}
            </Trans>
          }
          {...(job.totalBytes > 0 ? { value: percentOf(job.doneBytes, job.totalBytes) } : {})}
        />
      ) : (
        <Notice tone={refused.length > 0 ? "attention" : "info"}>
          <span className="flex flex-col gap-1">
            {job.results.map((result, index) => (
              <span key={index}>
                <Outcome result={result} />
              </span>
            ))}
          </span>
        </Notice>
      )}
    </>
  );
}

function Outcome({ result }: { result: ImportResult }) {
  const name = result.name;
  const reason = serverText(result.reason?.code, result.reason?.args, result.reasonText ?? "");

  switch (result.outcome) {
    case "Added":
      return <Trans>{name} is in the library now.</Trans>;
    case "Existing":
      return <Trans>{name} was in the library already.</Trans>;
    case "Refused":
      return (
        <Trans>
          {name} was not imported. {reason}
        </Trans>
      );
  }
}
