// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

interface AssignBlockersProps {
  sequencesFailed: boolean;
  optionsFailed: boolean;
  noSequence: boolean;
  noneRunnable: boolean;
}

// Why the dialog cannot offer an assignment: a list or the settings that could not be read, or no sequence that can
// run.
export function AssignBlockers({
  sequencesFailed,
  optionsFailed,
  noSequence,
  noneRunnable,
}: AssignBlockersProps) {
  return (
    <>
      {sequencesFailed ? (
        <Notice tone="fail">
          <Trans>The task sequence list could not be loaded.</Trans>
        </Notice>
      ) : null}
      {optionsFailed ? (
        <Notice tone="fail">
          <Trans>
            The deployment settings could not be loaded, so DDT cannot say what the assignment does.
            Close this and try again.
          </Trans>
        </Notice>
      ) : null}
      {noSequence ? (
        <p>
          <Trans>
            No task sequence exists yet. An administrator creates one under Deployment, Task
            sequences.
          </Trans>
        </p>
      ) : null}
      {noneRunnable ? (
        <p>
          <Trans>
            Every task sequence has problems, so none can run. An administrator fixes them under
            Deployment, Task sequences.
          </Trans>
        </p>
      ) : null}
    </>
  );
}
