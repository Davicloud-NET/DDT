import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import { machinesQuery, upsertMachine, type MachineSummary } from "./machines";

function machine(id: string, state: MachineSummary["state"], firstSeenUtc: string): MachineSummary {
  return {
    id,
    state,
    smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    manufacturer: null,
    model: null,
    serialNumber: null,
    assignedName: null,
    agentVersion: null,
    firstSeenUtc,
    lastSeenUtc: firstSeenUtc,
    lastSeenAddress: null,
    signedInBy: null,
  };
}

describe("upsertMachine", () => {
  it("keeps rows in place when a machine checks in and puts waiting machines first", () => {
    const queryClient = new QueryClient();
    const older = machine("a", "Approved", "2026-09-16T08:00:00Z");
    const newer = machine("b", "Approved", "2026-09-16T09:00:00Z");
    const waiting = machine("c", "Pending", "2026-09-16T07:00:00Z");

    queryClient.setQueryData(machinesQuery.queryKey, [waiting, newer, older]);

    upsertMachine(queryClient, { ...older, lastSeenUtc: "2026-09-16T10:00:00Z" });
    expect(
      queryClient.getQueryData<MachineSummary[]>(machinesQuery.queryKey)?.map((m) => m.id),
    ).toEqual(["c", "b", "a"]);

    upsertMachine(queryClient, { ...waiting, state: "Approved" });
    expect(
      queryClient.getQueryData<MachineSummary[]>(machinesQuery.queryKey)?.map((m) => m.id),
    ).toEqual(["b", "a", "c"]);
  });
});
