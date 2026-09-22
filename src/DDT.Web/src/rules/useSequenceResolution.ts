// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";

import type { MachineSummary } from "@/machines/machines";

import { sequenceResolutionQuery } from "./rules";

// What chooses the machine's sequence. An approval, an assignment or the end of a run changes it, and the machine
// list follows those live, so it is read again when the machine's state or run changes.
export function useSequenceResolution(machineId: string, machine: MachineSummary | null) {
  const queryClient = useQueryClient();
  const resolution = useQuery(sequenceResolutionQuery(machineId));

  const run = machine?.deployment ?? null;
  const facts = machine === null ? null : `${machine.state} ${run?.id ?? ""} ${run?.state ?? ""}`;
  const lastFacts = useRef(facts);

  useEffect(() => {
    const previous = lastFacts.current;
    lastFacts.current = facts;

    if (previous !== null && previous !== facts) {
      void queryClient.invalidateQueries({ queryKey: sequenceResolutionQuery(machineId).queryKey });
    }
  }, [facts, machineId, queryClient]);

  return resolution;
}
