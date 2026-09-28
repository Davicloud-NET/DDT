// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { EmptyState } from "@/ui/EmptyState";

// An empty image library, with how images get into it.
export function NoImages({ canEdit }: { canEdit: boolean }) {
  return (
    <EmptyState title={<Trans>No images yet</Trans>}>
      {canEdit ? (
        <Trans>
          Upload a WIM file to add its Windows images, or a disk image such as a Linux cloud image.
          A task sequence then applies or writes one.
        </Trans>
      ) : (
        <Trans>An administrator adds images by uploading WIM files or disk images here.</Trans>
      )}
    </EmptyState>
  );
}
