// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";

import { useLiveMarks } from "@/live/useLiveMarks";

import { advance, demoQuery, demoTones, type DemoMachine } from "./liveDemo";

// The demo's machines, their live marks, and the changes the demo patches into the cache.
export function useLiveDemo() {
  const queryClient = useQueryClient();
  const machines = useQuery(demoQuery).data ?? [];
  const mark = useLiveMarks({
    queryKey: demoQuery.queryKey,
    items: (list) => list,
    id: (machine) => machine.id,
    signature: (machine) => machine.state,
    tone: (machine) => demoTones[machine.state],
  });

  const patch = (change: (machine: DemoMachine) => DemoMachine) => {
    queryClient.setQueryData(demoQuery.queryKey, (list) => list?.map(change));
  };

  const goOn = () => {
    patch(advance);
  };

  const fail = () => {
    patch((machine) => (machine.state === "Deploying" ? { ...machine, state: "Failed" } : machine));
  };

  const appear = () => {
    queryClient.setQueryData(demoQuery.queryKey, (list) =>
      list === undefined
        ? list
        : [
            {
              id: String(list.length + 1),
              name: `LAB-PC-${String(20 + list.length)}`,
              state: "Waiting" as const,
              step: 0,
              percent: 0,
            },
            ...list,
          ],
    );
  };

  const startWaiting = () => {
    patch((machine) =>
      machine.state === "Waiting" ? { ...machine, state: "Deploying" } : machine,
    );
  };

  return { machines, mark, goOn, fail, appear, startWaiting };
}
