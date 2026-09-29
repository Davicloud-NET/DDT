// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Link } from "@tanstack/react-router";

import type { SequenceSummary } from "../sequences";

// The sequence's name as the link to its editor, over its description.
export function SequenceName({ sequence }: { sequence: SequenceSummary }) {
  return (
    <span className="flex min-w-0 flex-col leading-tight">
      <Link
        to="/deployment/sequences/$sequenceId"
        params={{ sequenceId: sequence.id }}
        className="truncate type-label text-[16.5px] hover:underline"
      >
        {sequence.name}
      </Link>
      {sequence.description !== null ? (
        <span className="truncate type-small text-muted" title={sequence.description}>
          {sequence.description}
        </span>
      ) : null}
    </span>
  );
}
