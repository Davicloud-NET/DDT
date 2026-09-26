// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useRouterState } from "@tanstack/react-router";

import { locate } from "./navigation";

// Stands in for a page of the new UI that is not built yet, while the M6.5 rewrite is under way on its branch.
export function PendingPage() {
  const { i18n } = useLingui();
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const here = locate(pathname);

  return (
    <div className="flex flex-col gap-3 px-6 pt-5 pb-8">
      <h1 className="type-title">{here ? i18n._(here.page.label) : pathname}</h1>
      <p className="max-w-[65ch] text-ink-2">
        <Trans>This page is being rebuilt on the new design system.</Trans>
      </p>
    </div>
  );
}
