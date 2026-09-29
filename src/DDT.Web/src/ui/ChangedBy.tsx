// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

// When something changed and, where the server knows, who changed it. title is the full time, for hovering.
export function ChangedBy({
  when,
  by,
  title,
  className,
}: {
  when: string;
  by: string | null;
  title: string;
  className?: string;
}) {
  return (
    <span className={className} title={title}>
      {by === null ? (
        when
      ) : (
        <Trans>
          {when} by {by}
        </Trans>
      )}
    </span>
  );
}
