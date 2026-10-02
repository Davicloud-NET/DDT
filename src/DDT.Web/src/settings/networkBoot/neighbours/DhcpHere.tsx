// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRef } from "react";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";

import { ReauthDialog } from "../../parts/ReauthDialog";
import { useGuardedAction } from "../../useGuardedAction";

import { neighboursQuery, setDhcpPxe, type NetbootNeighbours } from "./neighbours";

// Microsoft's DHCP server on the server's computer needs UDP 67 for itself, so DDT leaves that port to it and answers
// on 4011 alone. Option 60 on that DHCP server sends the machines there.
export function DhcpHere({ neighbours }: { neighbours: NetbootNeighbours }) {
  const queryClient = useQueryClient();
  // What was asked for, for the request that goes out before the next render and again after the password
  const wanted = useRef(true);
  const change = useGuardedAction({
    send: () => setDhcpPxe(wanted.current),
    onDone: (now) => {
      queryClient.setQueryData(neighboursQuery.queryKey, now);
    },
    askFirst: true,
  });
  const sends = neighbours.dhcpSendsPxe === true;
  // WDS on the same computer answers on 4011 itself, and option 60 is its own then
  const canChange =
    neighbours.helper && !neighbours.wds.running && typeof neighbours.dhcpSendsPxe === "boolean";

  return (
    <>
      <Notice
        tone={sends ? "info" : "attention"}
        actions={
          canChange ? (
            <Button
              size="sm"
              isDisabled={change.busy}
              onPress={() => {
                wanted.current = !sends;
                change.start();
              }}
            >
              {sends ? (
                <Trans>Stop sending option 60</Trans>
              ) : (
                <Trans>Set option 60 on this DHCP server</Trans>
              )}
            </Button>
          ) : null
        }
      >
        {sends ? (
          <Trans>
            Microsoft's DHCP server on this computer needs UDP 67 for itself, so DDT answers on port
            4011 alone. The DHCP server sends option 60, PXEClient, which brings machines that
            netboot there.
          </Trans>
        ) : (
          <Trans>
            Microsoft's DHCP server on this computer needs UDP 67 for itself, so DDT answers on port
            4011 alone. Machines that netboot find it there once the DHCP server sends option 60,
            PXEClient.
          </Trans>
        )}
      </Notice>
      {change.error === null ? null : <Notice tone="fail">{change.error}</Notice>}

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
