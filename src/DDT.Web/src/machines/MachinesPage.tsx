// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { isWaiting } from "@/deployments/deployments";
import { useMediaQuery } from "@/lib/useMediaQuery";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { machinesQuery } from "@/machines/machines";
import { useMachineActions } from "@/machines/useMachineActions";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";

import { MachineList } from "./list/MachineList";
import { MachineListHeader } from "./list/MachineListHeader";
import { straysOffer } from "./list/strays";
import { useMachineFilters } from "./list/useMachineFilters";
import { MachineActionErrors } from "./MachineActionErrors";
import { MachinePanel } from "./MachinePanel";
import { byAttention, inFilter, machineTag, matchesSearch } from "./machineView";
import { useCanDecide } from "./useCanDecide";

// Every machine that netbooted, patched in place from the hub's pushes. A machine whose state changed flashes in its
// new state's colour, and a new machine animates in. The list is only read on a timer while the live connection is
// down.
export function MachinesPage() {
  const { filter, query, selectedId, setSearch } = useMachineFilters();
  const live = useLiveStatus();
  const machines = useQuery({ ...machinesQuery, ...liveListOptions(live) });
  const canDecide = useCanDecide();
  const now = useNow(5_000);
  const narrow = useMediaQuery("(max-width: 767px)");
  const actions = useMachineActions();
  const mark = useLiveMarks({
    queryKey: machinesQuery.queryKey,
    items: (list) => list,
    id: (machine) => machine.id,
    // When a run starts waiting for someone, the row flashes in the waiting colour.
    signature: (machine) => `${machine.state} ${String(isWaiting(machine.deployment))}`,
    tone: (machine) => machineTag(machine).tone,
  });

  const all = machines.data ?? [];
  const matching = all.filter((machine) => matchesSearch(machine, query));
  const shown = matching.filter((machine) => inFilter(machine, filter)).sort(byAttention);
  const selected = all.find((machine) => machine.id === selectedId) ?? null;
  const strays = straysOffer(all);

  return (
    <Page>
      <MachineListHeader filter={filter} query={query} matching={matching} onChange={setSearch} />

      {machines.isError ? (
        <Notice tone="fail">
          <Trans>The machine list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <div className="flex items-start gap-4">
        <MachineList
          isPending={machines.isPending}
          isEmpty={all.length === 0}
          narrow={narrow}
          rows={{ machines: shown, now, canDecide, actions, strays, mark }}
          selectedId={selected === null ? null : selected.id}
          onSelect={(id) => {
            setSearch({ selected: id });
          }}
          onShowAll={() => {
            setSearch({ state: "all", q: "" });
          }}
        />

        {selected !== null ? (
          <MachinePanel
            machine={selected}
            actions={actions}
            canDecide={canDecide}
            now={now}
            onClose={() => {
              setSearch({ selected: undefined });
            }}
          />
        ) : null}
      </div>

      <MachineActionErrors actions={actions} />
    </Page>
  );
}
