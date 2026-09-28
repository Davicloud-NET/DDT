// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { rescanPxe, type PxeForm } from "../networkBoot";
import { putSection } from "../settings";

// Every host scans its interfaces and applies the section again, which restarts the listeners, so it asks first.
// The answer is the section with the new apply states.
export function RescanButton({ form }: { form: PxeForm }) {
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);
  const rescan = useMutation({
    mutationFn: rescanPxe,
    onSuccess: (view) => {
      putSection(queryClient, view);
      setAsking(false);
    },
  });

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Button
        size="sm"
        isDisabled={form.dirty || rescan.isPending}
        onPress={() => {
          rescan.reset();
          setAsking(true);
        }}
      >
        <Trans>Scan interfaces again</Trans>
      </Button>
      <span className="type-small text-muted">
        {form.dirty ? (
          <Trans>Save or discard your changes first.</Trans>
        ) : (
          <Trans>For a network adapter that was added, or an address that changed.</Trans>
        )}
      </span>
      <ConfirmDialog
        isOpen={asking}
        onOpenChange={setAsking}
        title={<Trans>Scan the interfaces again?</Trans>}
        confirmLabel={<Trans>Scan again</Trans>}
        onConfirm={() => {
          rescan.mutate();
        }}
        isBusy={rescan.isPending}
        error={rescan.error?.message}
      >
        <p>
          <Trans>
            Every host that runs network boot looks for its network adapters again and restarts its
            listeners with these settings. TFTP transfers in progress end, and those machines start
            their download again.
          </Trans>
        </p>
      </ConfirmDialog>
    </div>
  );
}
