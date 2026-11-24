// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { MachineRecordsView, RecoveryKeyView } from "@/machines/records/machineRecords";
import { nth, press } from "@/test/aria";
import { administrator, operator, viewer } from "@/test/builders";
import type { Routes } from "@/test/server";

import { answers, machine, machineId, open } from "./MachinePage.fixtures";

const current: RecoveryKeyView = {
  id: "0193a4b2-0000-7000-8000-0000000000a1",
  protectorId: "8f0a1c2d-3e4f-4a5b-9c6d-7e8f9a0b1c2d",
  volume: "C:",
  runId: "0193a4b2-0000-7000-8000-0000000000d2",
  createdUtc: "2026-09-16T10:04:00Z",
  replacedUtc: null,
};

const replaced: RecoveryKeyView = {
  ...current,
  id: "0193a4b2-0000-7000-8000-0000000000a0",
  protectorId: "11111111-2222-4333-8444-555555555555",
  createdUtc: "2026-09-10T08:00:00Z",
  replacedUtc: "2026-09-16T10:04:00Z",
};

function records(overrides: Partial<MachineRecordsView> = {}): MachineRecordsView {
  return { machineId, recoveryKeys: [current, replaced], hardwareHash: null, ...overrides };
}

function routes(extra: Routes = {}, shown: MachineRecordsView = records()): Routes {
  return {
    ...answers([machine({ state: "Done", deployment: null })], [], []),
    [`GET /api/machines/${machineId}/records`]: { body: shown },
    ...extra,
  };
}

function keyList(): HTMLElement {
  return screen.getByRole("list", { name: "Recovery keys" });
}

describe("MachinePage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("recovery keys", () => {
    it("shows that keys exist, without buttons, to anyone but an administrator", async () => {
      await open(routes(), { user: viewer });

      expect(await screen.findByText("8F0A1C2D-3E4F-4A5B-9C6D-7E8F9A0B1C2D")).toBeInTheDocument();
      expect(within(keyList()).getAllByRole("listitem")).toHaveLength(2);
      expect(within(keyList()).getByText("Replaced")).toBeInTheDocument();
      expect(within(keyList()).queryByRole("button")).not.toBeInTheDocument();
    });

    it("reveals a key's password to an administrator", async () => {
      const password = "123453-234564-345675-456786-567897-678909-111111-222222";
      const reveal = `POST /api/machines/${machineId}/recovery-keys/${current.id}/reveal`;
      const { server } = await open(routes({ [reveal]: { body: { password } } }), {
        user: administrator,
      });

      await screen.findByText("8F0A1C2D-3E4F-4A5B-9C6D-7E8F9A0B1C2D");
      press(nth(within(keyList()).getAllByRole("button", { name: "Reveal" }), 0));

      expect(await screen.findByText(password)).toBeInTheDocument();
      expect(server.count(reveal)).toBe(1);
    });

    it("deletes a replaced key once confirmed, and offers no delete for the current one", async () => {
      const remove = `DELETE /api/machines/${machineId}/recovery-keys/${replaced.id}`;
      const { server } = await open(routes({ [remove]: { status: 204 } }), {
        user: administrator,
      });

      await screen.findByText("8F0A1C2D-3E4F-4A5B-9C6D-7E8F9A0B1C2D");
      expect(within(keyList()).getAllByRole("button", { name: "Delete" })).toHaveLength(1);
      press(within(keyList()).getByRole("button", { name: "Delete" }));

      const dialog = await screen.findByRole("dialog", {
        name: "Delete the key 11111111-2222-4333-8444-555555555555?",
      });
      press(within(dialog).getByRole("button", { name: "Delete" }));

      await waitFor(() => {
        expect(within(keyList()).getAllByRole("listitem")).toHaveLength(1);
      });
      expect(server.count(remove)).toBe(1);
    });

    it("takes a key the hub pushes without reading the records again", async () => {
      const read = `GET /api/machines/${machineId}/records`;
      const { server, hub } = await open(routes({}, records({ recoveryKeys: [current] })));

      await screen.findByText("8F0A1C2D-3E4F-4A5B-9C6D-7E8F9A0B1C2D");
      act(() => {
        hub?.push("machineRecordsChanged", records());
      });

      expect(await screen.findByText("11111111-2222-4333-8444-555555555555")).toBeInTheDocument();
      expect(server.count(read)).toBe(1);
    });
  });

  it("shows the Autopilot hash's serial number and upload", async () => {
    await open(
      routes(
        {},
        records({
          recoveryKeys: [],
          hardwareHash: {
            serialNumber: "SN-4711",
            groupTag: "Kiosk",
            collectedUtc: "2026-09-16T10:04:00Z",
            uploadState: "Failed",
            uploadMessage: "ZtdDeviceAlreadyAssigned",
          },
        }),
      ),
    );

    expect(await screen.findByText("SN-4711")).toBeInTheDocument();
    expect(screen.getByText("Kiosk")).toBeInTheDocument();
    expect(screen.getByText("Failed: ZtdDeviceAlreadyAssigned")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Recovery keys" })).not.toBeInTheDocument();
  });

  describe("Autopilot", () => {
    const failed = records({
      recoveryKeys: [],
      hardwareHash: {
        serialNumber: "SN-4711",
        groupTag: null,
        collectedUtc: "2026-09-16T10:04:00Z",
        uploadState: "Failed",
        uploadMessage: "ZtdDeviceAlreadyAssigned",
      },
    });

    it("lets an operator upload a failed hash again, and shows it waiting", async () => {
      const upload = `POST /api/machines/${machineId}/autopilot/upload`;
      const { server } = await open(routes({ [upload]: { status: 204 } }, failed), {
        user: operator,
      });

      press(await screen.findByRole("button", { name: "Upload to Autopilot" }));

      expect(await screen.findByText("Waiting")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Upload to Autopilot" })).not.toBeInTheDocument();
      expect(server.count(upload)).toBe(1);
    });

    it("shows a viewer the hash without buttons", async () => {
      await open(routes({}, failed), { user: viewer });

      expect(await screen.findByText("SN-4711")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Download CSV" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Upload to Autopilot" })).not.toBeInTheDocument();
    });
  });
});
