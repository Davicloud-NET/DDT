// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";

import { importShare, isChosen, mdtShareQuery } from "./imports";
import { MdtShareChoices } from "./MdtShareChoices";

// What an MDT deployment share holds, to choose from: its image files, driver groups and applications. Opens for a path
// and closes once the import has started, which the panel then follows.
export function MdtShareDialog({ path, onClose }: { path: string | null; onClose: () => void }) {
  const share = useQuery({ ...mdtShareQuery(path ?? ""), enabled: path !== null });
  // What differs from the start, where everything the share can give is ticked but a disabled application.
  const [left, setLeft] = useState<ReadonlySet<string>>(new Set());
  const view = path === null ? undefined : share.data;
  const images = view?.imageFiles.filter((file) => file.found && !left.has(file.file)) ?? [];
  const groups = view?.driverGroups.filter((group) => !left.has(group.id)) ?? [];
  const applications =
    view?.applications.filter((application) => isChosen(application, left)) ?? [];

  const start = useMutation({
    mutationFn: () =>
      importShare(
        path ?? "",
        images.map((file) => file.file),
        groups.map((group) => group.id),
        applications.map((application) => application.id),
      ),
    onSuccess: close,
  });

  function close() {
    setLeft(new Set());
    start.reset();
    onClose();
  }

  return (
    <Dialog
      isOpen={path !== null}
      onOpenChange={(open) => {
        if (!open) {
          close();
        }
      }}
      title={<Trans>Import from an MDT deployment share</Trans>}
      width="lg"
      isBusy={start.isPending}
      footer={
        <>
          <Button variant="secondary" onPress={close}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            variant="primary"
            isDisabled={
              start.isPending || images.length + groups.length + applications.length === 0
            }
            onPress={() => {
              start.mutate();
            }}
          >
            <Trans>Import</Trans>
          </Button>
        </>
      }
    >
      {share.isError ? <Notice tone="fail">{share.error.message}</Notice> : null}
      {view === undefined ? (
        share.isError ? null : (
          <ListSkeleton padded={false} />
        )
      ) : (
        <MdtShareChoices
          view={view}
          left={left}
          onToggle={(id, chosen, byDefault = true) => {
            setLeft((current) => {
              const next = new Set(current);

              if (chosen === byDefault) {
                next.delete(id);
              } else {
                next.add(id);
              }

              return next;
            });
          }}
        />
      )}
      {start.isError ? <Notice tone="fail">{start.error.message}</Notice> : null}
    </Dialog>
  );
}
