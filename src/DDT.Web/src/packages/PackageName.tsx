// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { StateTag } from "@/ui/StateTag";

import type { PackageSummary } from "./packages";

// A package's row header: its name, whether it goes into the boot image, and its description or file.
export function PackageName({ item }: { item: PackageSummary }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span className="flex min-w-0 items-center gap-2">
        <span className="truncate type-label text-ink">{item.name}</span>
        {item.bootImage ? (
          <StateTag tone="idle">
            <Trans>Windows PE</Trans>
          </StateTag>
        ) : null}
      </span>
      <span className="truncate type-small text-muted">
        {item.description ?? item.originalFileName ?? ""}
      </span>
    </span>
  );
}
