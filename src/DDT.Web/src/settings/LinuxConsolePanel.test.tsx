// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { json, reads } from "@/test/serve";

import {
  agentView,
  expectAccessible,
  proof,
  sent,
  serveServer,
  typePassword,
} from "./serverTesting";

const sha256 = "4b2d6f8a0c2e4b6d8f0a2c4e6b8d0f2a3f7a9c1e5b0d2f4a6c8e0b2d4f6a8c0e";
const upload = "/api/settings/agent/linux/console";

async function choose(file: File): Promise<void> {
  const input = (await screen.findByRole("heading", { name: "Upload the console for Linux" }))
    .closest("section")
    ?.querySelector('input[type="file"]');

  if (!input) {
    throw new Error("The upload has no file input.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

describe("LinuxConsolePanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // First in this file, so no proof of identity is held yet.
  it("asks for the password before sending the console for Linux, and shows the answer", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "GET /api/settings/agent/linux/console": () =>
          json(agentView({ sha256, size: 30_123_456, source: "Bundled", version: "26.1.540" })),
        "POST /api/settings/reauthenticate": () => proof("linux-console-proof"),
        "PUT /api/settings/agent/linux/console": () =>
          json(
            agentView({
              sha256,
              size: 30_223_456,
              uploadedUtc: new Date().toISOString(),
              uploadedBy: "admin",
              source: "Uploaded",
              version: "26.1.541",
            }),
          ),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The console for Linux this server came with"),
    ).toBeInTheDocument();
    expect(screen.getByText("26.1.540")).toBeInTheDocument();

    await choose(new File([new Uint8Array([0x50, 0x4b, 3, 4])], "ddt-console-linux-x64.zip"));
    const confirm = await screen.findByRole("dialog", {
      name: "Upload ddt-console-linux-x64.zip as the console for Linux?",
    });
    fireEvent.click(within(confirm).getByRole("button", { name: "Upload console" }));

    await screen.findByRole("dialog", { name: "Confirm it is you" });
    await typePassword();

    expect(
      await screen.findByText("Uploaded. Machines with the agent for Linux show it from now on."),
    ).toBeInTheDocument();
    expect(screen.getByText("The console for Linux uploaded here")).toBeInTheDocument();
    expect(sent(requests, "PUT", upload).map((request) => request.headers)).toEqual([
      expect.objectContaining({
        "content-type": "application/zip",
        "x-ddt-reauthentication": "linux-console-proof",
      }),
    ]);
    expect(sent(requests, "PUT", "/api/settings/agent/console")).toEqual([]);
    expect(reads(requests, upload)).toBe(1);

    await expectAccessible();
  });

  it("shows what machines show when the server has no console for Linux", async () => {
    serveServer({ "GET /api/settings/agent": () => json(agentView()) }, { tab: "agent" });

    expect(
      await screen.findByText("The console the Linux boot environment brings"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Drop ddt-console-linux-x64.zip here, or choose it."),
    ).toBeInTheDocument();
  });
});
