// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { useIsAdministrator } from "@/auth/useIsAdministrator";
import { formattingLocale } from "@/i18n/i18n";
import { relativeTime } from "@/lib/relativeTime";
import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";

import {
  bootImageQuery,
  serveBuild,
  type BootImageStoredBuild,
  type BootImageView,
} from "./bootImage";

// The builds the boot directory holds: the one machines netboot, and what an administrator can go back to.
export function StoredBuilds({ view, now }: { view: BootImageView; now: number }) {
  const queryClient = useQueryClient();
  const administrator = useIsAdministrator();
  const [chosen, setChosen] = useState<BootImageStoredBuild | null>(null);
  const serve = useMutation({
    mutationFn: serveBuild,
    onSuccess: (answer) => {
      queryClient.setQueryData(bootImageQuery.queryKey, answer);
    },
  });
  const running = view.job?.state === "Running";

  return (
    <Panel title={<Trans>Builds on the server</Trans>} flush>
      <ul className="flex flex-col">
        {view.builds.map((build) => (
          <li
            key={build.name ?? ""}
            className="flex min-h-13 items-center gap-3 border-b border-line-soft px-4 py-2.5 last:border-b-0"
          >
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="truncate type-label text-ink">
                <BuildTime build={build} now={now} />
              </span>
              <span className="truncate type-data text-muted">
                {build.name ?? <Trans>Copied into the boot directory by hand</Trans>}
              </span>
            </span>
            {build.current ? (
              <StateTag tone="ok">
                <Trans>Served</Trans>
              </StateTag>
            ) : administrator ? (
              <Button
                size="sm"
                isDisabled={running || serve.isPending}
                onPress={() => {
                  serve.reset();
                  setChosen(build);
                }}
              >
                <Trans>Serve this build</Trans>
              </Button>
            ) : null}
          </li>
        ))}
      </ul>
      {serve.isError ? (
        <Notice tone="fail" className="m-4">
          {serve.error.message}
        </Notice>
      ) : null}

      <ConfirmDialog
        isOpen={chosen !== null}
        onOpenChange={(open) => {
          if (!open) {
            setChosen(null);
          }
        }}
        title={<Trans>Serve this build?</Trans>}
        confirmLabel={<Trans>Serve this build</Trans>}
        onConfirm={() => {
          if (chosen !== null) {
            serve.mutate(chosen.name);
          }

          setChosen(null);
        }}
      >
        <p>
          <Trans>
            Machines that netboot from now on get this build. A machine that is netbooting right now
            finishes with the build it started.
          </Trans>
        </p>
      </ConfirmDialog>
    </Panel>
  );
}

function BuildTime({ build, now }: { build: BootImageStoredBuild; now: number }) {
  if (build.builtUtc === null) {
    return <Trans>Built at a time not recorded</Trans>;
  }

  const when = relativeTime(build.builtUtc, now);

  return (
    <span title={new Date(build.builtUtc).toLocaleString(formattingLocale())}>
      <Trans>Built {when}</Trans>
    </span>
  );
}
