// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { Skeleton } from "@/ui/Skeleton";

import { MachineCards } from "./MachineCards";
import type { MachineRowsProps } from "./machineRows";
import { MachineTable } from "./MachineTable";

interface MachineListProps {
  isPending: boolean;
  // No machine registered at all, rather than none the filters leave.
  isEmpty: boolean;
  // A phone's width, from useMediaQuery.
  narrow: boolean;
  rows: MachineRowsProps;
  selectedId: string | null;
  onSelect: (id: string | undefined) => void;
  onShowAll: () => void;
}

// The list's surface: loading, why it is empty, or the machines as a table, or as a list on a phone.
export function MachineList({
  isPending,
  isEmpty,
  narrow,
  rows,
  selectedId,
  onSelect,
  onShowAll,
}: MachineListProps) {
  return (
    <section className="min-w-0 flex-1 overflow-hidden rounded-panel bg-panel shadow-panel">
      {isPending ? (
        <LoadingRows />
      ) : isEmpty ? (
        <EmptyState title={<Trans>No machines yet</Trans>}>
          <Trans>
            Machines appear here on their own when they netboot on a network DDT answers, within a
            few seconds of starting. Nothing is erased until someone signs in at the machine or
            assigns it a sequence here.
          </Trans>
        </EmptyState>
      ) : rows.machines.length === 0 ? (
        <EmptyState
          title={<Trans>No machine matches</Trans>}
          action={
            <Button onPress={onShowAll}>
              <Trans>Show all machines</Trans>
            </Button>
          }
        />
      ) : narrow ? (
        // A table would scroll sideways on a phone and hide the machines' states.
        <MachineCards {...rows} />
      ) : (
        <MachineTable {...rows} selectedId={selectedId} onSelect={onSelect} />
      )}
    </section>
  );
}

function LoadingRows() {
  return (
    <div aria-hidden="true" className="flex flex-col">
      <div className="h-10 border-b border-line" />
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className="flex h-14 items-center gap-3 border-b border-line-soft px-4">
          <Skeleton className="size-9.5" />
          <span className="flex w-60 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-36" />
            <Skeleton className="h-3 w-48" />
          </span>
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-3.5 flex-1" />
        </div>
      ))}
    </div>
  );
}
