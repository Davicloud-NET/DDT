// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { GroupDescription } from "./groupDescription";

export function GroupName({ description }: { description: GroupDescription }) {
  if (description === null) {
    return null;
  }

  return description === "notFound" ? (
    <span className="type-label text-attention-text">
      <Trans>Not found in the directory</Trans>
    </span>
  ) : (
    <span className="type-label text-ink">{description.name}</span>
  );
}
