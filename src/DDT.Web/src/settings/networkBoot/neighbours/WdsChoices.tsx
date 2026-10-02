// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";

import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";

import { rescanPxe } from "../../networkBoot";
import { ReauthDialog } from "../../parts/ReauthDialog";
import { putSection } from "../../settings";
import { useGuardedAction } from "../../useGuardedAction";

import { changeWds, neighboursQuery, type NetbootNeighbours, type WdsChange } from "./neighbours";
import { WdsState } from "./WdsState";

// Windows Deployment Services on the same computer holds the TFTP and PXE ports. DDT either takes its place, or goes
// into its boot menu next to MDT's LiteTouch, which lets a shop try DDT without touching its network.
export function WdsChoices({ neighbours }: { neighbours: NetbootNeighbours }) {
  const queryClient = useQueryClient();
  const [asked, setAsked] = useState<WdsChange | null>(null);
  const [done, setDone] = useState<WdsChange | null>(null);
  // What was asked for, for the request that goes out before the next render and again after the password
  const wanted = useRef<WdsChange>("boot-image");
  const change = useGuardedAction({
    send: () => changeWds(wanted.current),
    onDone: (now) => {
      queryClient.setQueryData(neighboursQuery.queryKey, now);
      setDone(wanted.current);

      // DDT binds the ports WDS let go of once its listeners start again
      if (wanted.current === "replace") {
        void rescanPxe().then((view) => {
          putSection(queryClient, view);
        });
      }
    },
    askFirst: true,
  });
  const start = (action: WdsChange) => {
    wanted.current = action;
    setAsked(action);
    setDone(null);
    change.start();
  };

  return (
    <>
      <WdsState
        neighbours={neighbours}
        isBusy={change.busy}
        onChoose={(wanted) => {
          // Stopping WDS asks first
          if (wanted === "replace") {
            setAsked("replace");
          } else {
            start(wanted);
          }
        }}
      />
      {change.error === null ? null : <Notice tone="fail">{change.error}</Notice>}
      {done === "boot-image" ? (
        <Notice>
          <Trans>
            The WDS boot menu offers DDT now, and gets each new boot image when it is built.
          </Trans>
        </Notice>
      ) : null}

      <ConfirmDialog
        isOpen={asked === "replace" && !change.busy && !change.needsReauth && done !== "replace"}
        onOpenChange={(open) => {
          if (!open) {
            setAsked(null);
          }
        }}
        title={<Trans>Use DDT in place of WDS?</Trans>}
        confirmLabel={<Trans>Stop WDS</Trans>}
        danger
        onConfirm={() => {
          start("replace");
        }}
      >
        <p>
          <Trans>
            DDT stops Windows Deployment Services and keeps it from starting with Windows, then
            answers netboot itself. MDT's LiteTouch no longer netboots from this server. WDS stays
            installed, and this page starts it again.
          </Trans>
        </p>
      </ConfirmDialog>

      <ReauthDialog
        isOpen={change.needsReauth}
        onAccepted={change.retryAfterReauth}
        onCancel={change.cancelReauth}
        confirmLabel={<Trans>Confirm and go on</Trans>}
        reason={
          <Trans>
            This changes what every machine that netboots from this server loads, so it needs your
            password again.
          </Trans>
        }
      />
    </>
  );
}
