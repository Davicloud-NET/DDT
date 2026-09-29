// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import type { CurrentUser } from "@/auth/auth";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { revokeToken, upsertToken, type ApiTokenView } from "./tokens";
import { revokedCopy } from "./tokenView";

// Asks before a token is revoked. The server answers with no content, so the lists patch in the revoked token
// themselves. The hub's copy replaces it moments later.
export function RevokeTokenDialog({
  token,
  me,
  onClose,
}: {
  token: ApiTokenView | null;
  me: CurrentUser;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();

  const revoke = useMutation({
    mutationFn: (revoked: ApiTokenView) => revokeToken(revoked.id),
    onSuccess: (_, revoked) => {
      upsertToken(queryClient, revokedCopy(revoked, me.userName, Date.now()), me.id);
      onClose();
    },
  });

  const name = token?.name ?? "";
  const owner = token?.userName ?? "";
  const own = token?.userId === me.id;

  return (
    <ConfirmDialog
      isOpen={token !== null}
      onOpenChange={(open) => {
        if (!open) {
          revoke.reset();
          onClose();
        }
      }}
      title={
        own ? (
          <Trans>Revoke {name}?</Trans>
        ) : (
          <Trans>
            Revoke {name} of {owner}?
          </Trans>
        )
      }
      confirmLabel={<Trans>Revoke token</Trans>}
      danger
      isBusy={revoke.isPending}
      error={revoke.isError ? revoke.error.message : undefined}
      onConfirm={() => {
        if (token !== null) {
          revoke.mutate(token);
        }
      }}
    >
      <p>
        <Trans>
          Whatever uses the token is refused from its next request. The token stays in the list as
          revoked and cannot be turned back on; make a new one if it is needed again.
        </Trans>
      </p>
    </ConfirmDialog>
  );
}
