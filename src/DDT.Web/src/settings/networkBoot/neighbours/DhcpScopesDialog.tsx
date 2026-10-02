// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { Dialog } from "@/ui/Dialog";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";

import { ReauthDialog } from "../../parts/ReauthDialog";
import { useGuardedAction } from "../../useGuardedAction";

import { dhcpScopesQuery, setDhcpOptions, type DhcpScope } from "./neighbours";

// The scopes of the DHCP server on this computer, to choose which send their machines to DDT.
export function DhcpScopesDialog({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const scopes = useQuery({ ...dhcpScopesQuery, enabled: isOpen });
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set());
  const set = useGuardedAction({
    send: () => setDhcpOptions([...chosen]),
    onDone: (now) => {
      queryClient.setQueryData(dhcpScopesQuery.queryKey, now);
      setChosen(new Set());
    },
    askFirst: true,
  });

  return (
    <>
      <Dialog
        isOpen={isOpen && !set.needsReauth}
        onOpenChange={(open) => {
          if (!open) {
            set.reset();
            setChosen(new Set());
            onClose();
          }
        }}
        title={<Trans>Options 66 and 67 on this DHCP server</Trans>}
        isBusy={set.busy}
        footer={
          <Button variant="primary" isDisabled={set.busy || chosen.size === 0} onPress={set.start}>
            <Trans>Set for the chosen scopes</Trans>
          </Button>
        }
      >
        {scopes.isError ? <Notice tone="fail">{scopes.error.message}</Notice> : null}
        {scopes.data === undefined ? (
          scopes.isError ? null : (
            <ListSkeleton padded={false} />
          )
        ) : scopes.data.length === 0 ? (
          <p className="text-ink-2">
            <Trans>The DHCP server has no scopes.</Trans>
          </p>
        ) : (
          scopes.data.map((scope) => (
            <Checkbox
              key={scope.scopeId}
              isSelected={chosen.has(scope.scopeId)}
              onChange={(selected) => {
                setChosen((current) => {
                  const next = new Set(current);

                  if (selected) {
                    next.add(scope.scopeId);
                  } else {
                    next.delete(scope.scopeId);
                  }

                  return next;
                });
              }}
            >
              <ScopeLine scope={scope} />
            </Checkbox>
          ))
        )}
        {set.error === null ? null : <Notice tone="fail">{set.error}</Notice>}
      </Dialog>

      <ReauthDialog
        isOpen={set.needsReauth}
        onAccepted={set.retryAfterReauth}
        onCancel={set.cancelReauth}
        confirmLabel={<Trans>Confirm and set</Trans>}
        reason={
          <Trans>
            These options decide what every machine in the scope loads when it netboots, so setting
            them needs your password again.
          </Trans>
        }
      />
    </>
  );
}

function ScopeLine({ scope }: { scope: DhcpScope }) {
  const server = scope.bootServer ?? "?";
  const file = scope.bootFile ?? "?";

  return (
    <span className="flex min-w-0 flex-col">
      <span>
        {scope.scopeId} {scope.name}
      </span>
      <span className="type-small text-muted">
        {scope.bootServer === null && scope.bootFile === null ? (
          <Trans>Sets neither option yet.</Trans>
        ) : (
          <Trans>
            Sends machines to {server} for {file} now.
          </Trans>
        )}
        {scope.active ? null : (
          <>
            {" "}
            <Trans>The scope is not active.</Trans>
          </>
        )}
      </span>
    </span>
  );
}
