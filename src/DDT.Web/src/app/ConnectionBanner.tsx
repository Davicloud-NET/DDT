// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useEffect, useState, useSyncExternalStore } from "react";

import type { LiveConnection } from "@/live/liveConnection";

// Nothing shows while the live connection is up. Only a connection lost for a few seconds earns a banner, so a
// short reconnect or the first connect never flashes one.
export function ConnectionBanner({ live }: { live: LiveConnection }) {
  const status = useSyncExternalStore(live.onStatusChange, live.status);
  const [shown, setShown] = useState(false);

  useEffect(() => {
    const live = status === "live";
    const timer = window.setTimeout(
      () => {
        setShown(!live);
      },
      live ? 0 : 4000,
    );

    return () => {
      window.clearTimeout(timer);
    };
  }, [status]);

  if (!shown) {
    return null;
  }

  return (
    <div
      role="status"
      className="shrink-0 border-b border-line bg-attention px-6 py-2 type-small text-on-attention"
    >
      <Trans>
        The live connection to the server is lost. DDT keeps trying to reconnect; until then, pages
        refresh every few seconds.
      </Trans>
    </div>
  );
}
