// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";

// What an empty list shows. Administrators start from a template. Everyone else waits for one.
export function NoSequences({
  isAdministrator,
  onCreate,
}: {
  isAdministrator: boolean;
  onCreate: () => void;
}) {
  return (
    <EmptyState
      title={<Trans>No task sequences yet</Trans>}
      action={
        isAdministrator ? (
          <Button variant="primary" onPress={onCreate}>
            <Trans>New task sequence</Trans>
          </Button>
        ) : null
      }
    >
      {isAdministrator ? (
        <Trans>
          Start from a template, such as Install Windows: it partitions the disk, applies an image,
          adds the drivers for the machine's model and writes the answer file. Or start empty and
          add the steps yourself.
        </Trans>
      ) : (
        <Trans>
          An administrator creates task sequences here. Until then, no machine can be given one.
        </Trans>
      )}
    </EmptyState>
  );
}
