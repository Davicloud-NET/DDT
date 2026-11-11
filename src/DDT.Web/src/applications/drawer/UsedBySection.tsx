// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId } from "react";

import type { ApplicationUse } from "../applications";
import { UsedBy } from "../list/UsedBy";

export function UsedBySection({ uses }: { uses: readonly ApplicationUse[] }) {
  const labelId = useId();

  return (
    <section aria-labelledby={labelId} className="flex flex-col gap-1.5 type-small">
      <span id={labelId} className="type-label text-ink">
        <Trans>Used by</Trans>
      </span>
      <UsedBy uses={uses} />
    </section>
  );
}
