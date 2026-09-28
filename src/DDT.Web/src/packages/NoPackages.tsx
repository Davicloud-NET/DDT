// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { EmptyState } from "@/ui/EmptyState";

// An empty driver or file library, with what a package of it is for.
export function NoPackages({ drivers }: { drivers: boolean }) {
  return (
    <EmptyState
      title={drivers ? <Trans>No driver packages yet</Trans> : <Trans>No file packages yet</Trans>}
    >
      {drivers ? (
        <Trans>
          Upload a zip of drivers and choose the models it is for. A task sequence with an Inject
          drivers step then adds them to those machines.
        </Trans>
      ) : (
        <Trans>
          Upload a zip of files, then choose it in a Run script step of a task sequence.
        </Trans>
      )}
    </EmptyState>
  );
}
