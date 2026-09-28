// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { continuesText, unansweredText, type WaitState } from "./waitState";

// What the run waits for, that the person at the machine can do the same, and why letting it go on failed.
export function WaitingText({ wait, error }: { wait: WaitState; error: string | null }) {
  const { paused, message, pausedAt, continues, inputs } = wait;

  return (
    <div className="flex min-w-0 flex-1 basis-80 flex-col gap-1">
      {paused ? (
        <p className="type-body">
          <strong className="font-bold">{pausedAt}</strong>
          {message === null || message === "" ? null : <> {message}</>}
        </p>
      ) : (
        <p className="type-body">
          <strong className="font-bold">
            <Trans>The run waits for answers before it starts.</Trans>
          </strong>
          {inputs.length > 0 ? <> {unansweredText(inputs.map((input) => input.label))}</> : null}
        </p>
      )}
      <p className="type-small">
        {paused ? (
          continues === null ? (
            <Trans>The person at the machine can let the run go on there, too.</Trans>
          ) : (
            continuesText(continues)
          )
        ) : (
          <Trans>The person at the machine can answer there, too.</Trans>
        )}
      </p>
      {error !== null ? (
        <p role="alert" className="type-small font-semibold">
          {error}
        </p>
      ) : null}
    </div>
  );
}
