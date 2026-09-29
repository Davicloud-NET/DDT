// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it, vi } from "vitest";

import type { MachineSummary } from "@/machines/machines";
import { machineSummary } from "@/test/builders";

import { cacheEventHandlers } from "./cacheEvents";
import type { EventHandler } from "./liveConnection";

// The handlers over a bare cache. emit hands a handler its payload the way the hub would.
function events() {
  const queryClient = new QueryClient();
  const handlers: Record<string, EventHandler> = cacheEventHandlers(queryClient);
  const emit = (event: string, payload: unknown) => {
    const handler = handlers[event];

    if (handler === undefined) {
      throw new Error(`No handler takes ${event}.`);
    }

    handler(payload as never);
  };

  return { queryClient, emit };
}

describe("cacheEventHandlers", () => {
  it("patches a changed machine into the list", () => {
    const { queryClient, emit } = events();
    queryClient.setQueryData(["machines"], [machineSummary({ id: "m1", state: "Pending" })]);

    emit("machineChanged", machineSummary({ id: "m1", state: "Approved" }));

    expect(queryClient.getQueryData<MachineSummary[]>(["machines"])?.[0]?.state).toBe("Approved");
  });

  it("drops removed machines from the list without reading it again", () => {
    const { queryClient, emit } = events();
    queryClient.setQueryData(
      ["machines"],
      ["m1", "m2", "m3"].map((id) => machineSummary({ id, state: "Pending" })),
    );
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    emit("machinesRemoved", { machineIds: ["m1", "m3"] });

    expect(
      queryClient.getQueryData<MachineSummary[]>(["machines"])?.map((listed) => listed.id),
    ).toEqual(["m2"]);
    expect(invalidate).not.toHaveBeenCalled();
  });

  it("takes a settings section and the interfaces of the pxe hosts from their events", () => {
    const { queryClient, emit } = events();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const machines = { section: "machines", version: 4, values: { maxWaiting: 50 } };

    emit("settingsChanged", machines);

    expect(queryClient.getQueryData(["settings", "machines"])).toEqual(machines);
    expect(invalidate.mock.calls).toEqual([[{ queryKey: ["settings-overview"] }]]);

    invalidate.mockClear();
    emit("settingsChanged", { section: "ldap", version: 2, values: {} });

    expect(invalidate.mock.calls).toEqual([
      [{ queryKey: ["settings-overview"] }],
      [{ queryKey: ["directory"] }],
    ]);

    invalidate.mockClear();
    const hosts = [
      {
        host: "ddt-01",
        updatedUtc: "2026-09-27T10:00:00Z",
        interfaces: [{ name: "lab", addresses: ["10.40.0.1"], served: true }],
        unmatched: [],
      },
    ];
    emit("pxeInterfacesChanged", hosts);

    expect(queryClient.getQueryData(["settings", "pxe", "interfaces"])).toEqual(hosts);
    expect(invalidate).not.toHaveBeenCalled();
  });

  it("takes an uploaded agent from its event, and reads the served certificate again when it changed", () => {
    const { queryClient, emit } = events();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const agent = {
      sha256: "ab12",
      size: 4096,
      uploadedUtc: "2026-09-27T10:00:00Z",
      uploadedBy: "admin",
      source: "Uploaded",
    };

    emit("agentChanged", agent);

    expect(queryClient.getQueryData(["settings-agent"])).toEqual(agent);
    expect(invalidate).not.toHaveBeenCalled();

    queryClient.setQueryData(["settings-certificate"], { subject: "CN=old", servedHere: true });
    emit("certificateChanged", { subject: "CN=new" });

    expect(queryClient.getQueryData(["settings-certificate"])).toEqual({
      subject: "CN=new",
      servedHere: true,
    });
    expect(invalidate.mock.calls).toEqual([[{ queryKey: ["server-certificate"] }]]);
  });

  it("takes the rules from their event, and reads again only what the rules choose", () => {
    const { queryClient, emit } = events();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const rules = [{ id: "r1", kind: "Model", model: "Latitude*", sequenceName: "Install" }];

    emit("rulesChanged", rules);

    expect(queryClient.getQueryData(["rules"])).toEqual(rules);
    expect(invalidate.mock.calls).toEqual([[{ queryKey: ["machine-sequence"] }]]);

    invalidate.mockClear();
    emit("sequenceChanged", { id: "s1", revision: 2, changedBy: "admin" });

    expect(invalidate.mock.calls).toEqual([
      [{ queryKey: ["sequences"] }],
      [{ queryKey: ["machine-sequence"] }],
      [{ queryKey: ["sequence", "s1"] }],
    ]);
  });

  it("takes the machine roles and the accounts from their events without reading their lists again", () => {
    const { queryClient, emit } = events();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const roles = [{ id: "role1", name: "Kiosk", values: [], revision: 2 }];
    const account = (id: string, name: string, revision = 1) => ({
      id,
      name,
      revision,
      password: { isSet: true, unreadable: false, updatedUtc: null },
      usedBy: [],
    });

    emit("rolesChanged", roles);

    expect(queryClient.getQueryData(["machine-roles"])).toEqual(roles);
    expect(invalidate.mock.calls).toEqual([[{ queryKey: ["machine-sequence"] }]]);

    invalidate.mockClear();
    queryClient.setQueryData(["accounts"], [account("a1", "Join"), account("a3", "Share")]);
    emit("accountChanged", account("a2", "Lab"));
    emit("accountChanged", account("a1", "Join", 2));

    expect(queryClient.getQueryData(["accounts"])).toEqual([
      account("a1", "Join", 2),
      account("a2", "Lab"),
      account("a3", "Share"),
    ]);

    emit("accountsRemoved", { accountIds: ["a2", "a3"] });

    expect(queryClient.getQueryData(["accounts"])).toEqual([account("a1", "Join", 2)]);
    expect(invalidate).not.toHaveBeenCalled();
  });

  it("reads an open sequence again only when the change is newer than its copy", () => {
    const { queryClient, emit } = events();
    queryClient.setQueryData(["sequence", "s1"], { id: "s1", revision: 3 });
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const readsOf = () =>
      invalidate.mock.calls.filter(([filters]) => filters?.queryKey?.[0] === "sequence").length;

    emit("sequenceChanged", { id: "s1", revision: 3, changedBy: "admin" });
    expect(readsOf()).toBe(0);

    emit("sequenceChanged", { id: "s1", revision: 4, changedBy: "other" });
    expect(readsOf()).toBe(1);

    emit("sequenceChanged", { id: "s1", revision: null, changedBy: "other" });
    expect(readsOf()).toBe(2);
  });

  it("patches the library from its events, and reads the sequences again only when something is gone", () => {
    const { queryClient, emit } = events();
    queryClient.setQueryData(["images"], [{ id: "i1", name: "Alpha" }]);
    queryClient.setQueryData(["packages"], [{ id: "p1", name: "Drivers" }]);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const reads = () => invalidate.mock.calls.map(([filters]) => filters?.queryKey);

    emit("imageChanged", { id: "i2", name: "Beta" });

    expect(queryClient.getQueryData(["images"])).toEqual([
      { id: "i1", name: "Alpha" },
      { id: "i2", name: "Beta" },
    ]);
    expect(reads()).toEqual([["image-uploads"]]);

    invalidate.mockClear();
    emit("imagesRemoved", { imageIds: ["i1"] });

    expect(queryClient.getQueryData(["images"])).toEqual([{ id: "i2", name: "Beta" }]);
    expect(reads()).toEqual([["sequences"], ["sequence"], ["machine-sequence"]]);

    invalidate.mockClear();
    emit("packageChanged", { id: "p1", name: "Drivers, new" });

    expect(queryClient.getQueryData(["packages"])).toEqual([{ id: "p1", name: "Drivers, new" }]);
    expect(reads()).toEqual([["image-uploads"]]);

    invalidate.mockClear();
    emit("packagesRemoved", { packageIds: ["p1"] });

    expect(queryClient.getQueryData(["packages"])).toEqual([]);
    expect(reads()).toEqual([["sequences"], ["sequence"], ["machine-sequence"]]);
  });
});
