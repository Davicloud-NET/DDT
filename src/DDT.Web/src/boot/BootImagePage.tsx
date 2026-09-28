// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";

import { bootImageQuery, type BootImageView } from "./bootImage";
import { BuildCommand } from "./BuildCommand";
import { LastBuild } from "./LastBuild";
import { ReplacedAnchor } from "./ReplacedAnchor";
import { WindowsPEDrivers } from "./WindowsPEDrivers";

// The WinPE boot image that machines netboot into. Shows what the last build put in it, whether the drivers flagged for
// WinPE changed since, and how to build it again.
export function BootImagePage() {
  const live = useLiveStatus();
  const boot = useQuery({ ...bootImageQuery, ...liveListOptions(live) });
  const administrator = useIsAdministrator();
  const now = useNow(60_000);

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Boot image</Trans>} />

      {administrator ? <ReplacedAnchor /> : null}

      {boot.isError ? (
        <Notice tone="fail">
          <Trans>The state of the boot image could not be read.</Trans>
        </Notice>
      ) : null}

      {boot.isPending ? (
        <Panel>
          <ListSkeleton padded={false} />
        </Panel>
      ) : boot.data !== undefined ? (
        <>
          <BuildState view={boot.data} />
          <div className="grid items-start gap-4 lg:grid-cols-2">
            <LastBuild view={boot.data} now={now} />
            <WindowsPEDrivers view={boot.data} />
          </div>
          <BuildCommand view={boot.data} />
        </>
      ) : null}
    </Page>
  );
}

function BuildState({ view }: { view: BootImageView }) {
  if (!view.stale) {
    return view.build === null ? null : (
      <Notice tone="info">
        <Trans>The boot image has every driver flagged for Windows PE.</Trans>
      </Notice>
    );
  }

  return (
    <Notice tone="attention">
      {view.build === null ? (
        <Trans>
          Drivers are flagged for Windows PE, but no boot image built with this version of the build
          script is in the boot directory. Build it again to add them.
        </Trans>
      ) : (
        <Trans>
          The drivers flagged for Windows PE have changed since the boot image was built. Build it
          again so that machines netboot with them.
        </Trans>
      )}
    </Notice>
  );
}
