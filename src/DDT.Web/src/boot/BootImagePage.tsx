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
import { BuilderPanel } from "./BuilderPanel";
import { BuildPanel } from "./BuildPanel";
import { BuildState } from "./BuildState";
import { JobPanel } from "./JobPanel";
import { LastBuild } from "./LastBuild";
import { ReplacedAnchor } from "./ReplacedAnchor";
import { StoredBuilds } from "./StoredBuilds";
import { WindowsPEDrivers } from "./WindowsPEDrivers";

// The WinPE boot image that machines netboot into. Shows what the served build has in it and whether it still fits the
// server, and builds it again: on a Windows server at a button, elsewhere with a command for a Windows PC.
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
          {boot.data.builder.available ? <BuildPanel view={boot.data} /> : null}
          {offersBuilder(boot.data) ? <BuilderPanel /> : null}
          {boot.data.builder.available || boot.data.builder.package ? null : (
            <BuildCommand view={boot.data} />
          )}
          {boot.data.job === null ? null : <JobPanel job={boot.data.job} now={now} />}
          {boot.data.builds.length > 1 ? <StoredBuilds view={boot.data} now={now} /> : null}
        </>
      ) : null}
    </Page>
  );
}

// A server that builds on its own needs no builder for another PC.
function offersBuilder(view: BootImageView): boolean {
  const adk = view.builder.adk;

  return view.builder.package && !(view.builder.available && adk?.installed && adk.supported);
}
