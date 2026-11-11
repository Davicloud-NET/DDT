// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { json, reads } from "@/test/serve";

import {
  agentView,
  expectAccessible,
  overview,
  proof,
  sent,
  serveServer,
  typePassword,
} from "./serverTesting";

const sha256 = "9c1e5b0d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f0a2c4e6b8d0f2a3f7a";
const upload = "/api/settings/agent/linux/binary";

// The first bytes of an ELF file, as the agent for Linux begins.
function agentFile(): File {
  return new File([new Uint8Array([0x7f, 0x45, 0x4c, 0x46, 2, 1, 1])], "ddt-agent-linux-x64");
}

async function choose(file: File): Promise<void> {
  const input = (await screen.findByRole("heading", { name: "Upload the agent for Linux" }))
    .closest("section")
    ?.querySelector('input[type="file"]');

  if (!input) {
    throw new Error("The upload has no file input.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

describe("LinuxAgentPanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // First in this file, so no proof of identity is held yet.
  it("asks for the password before sending the agent for Linux, and shows the answer without reading it again", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "GET /api/settings/agent/linux": () =>
          json(agentView({ sha256, size: 19_521_984, source: "Bundled", version: "26.1.512" })),
        "POST /api/settings/reauthenticate": () => proof("linux-agent-proof"),
        "PUT /api/settings/agent/linux/binary": () =>
          json(
            agentView({
              sha256,
              size: 19_534_304,
              uploadedUtc: new Date().toISOString(),
              uploadedBy: "admin",
              source: "Uploaded",
              version: "26.1.520",
            }),
          ),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The agent for Linux this server came with"),
    ).toBeInTheDocument();
    expect(screen.getByText("26.1.512")).toBeInTheDocument();
    expect(
      screen.getByText(
        /The agent runs as root on every machine that starts the Linux boot environment/,
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Remove the uploaded agent for Linux" }),
    ).not.toBeInTheDocument();

    await choose(agentFile());
    const confirm = await screen.findByRole("dialog", {
      name: "Upload ddt-agent-linux-x64 as the agent for Linux?",
    });
    fireEvent.click(within(confirm).getByRole("button", { name: "Upload agent" }));

    await screen.findByRole("dialog", { name: "Confirm it is you" });
    expect(sent(requests, "PUT", upload)).toEqual([]);
    await typePassword();

    expect(
      await screen.findByText(
        "Uploaded. Machines that start the Linux boot environment from now on run it.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("The agent for Linux uploaded here")).toBeInTheDocument();
    expect(screen.getByText("26.1.520")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Remove the uploaded agent for Linux" }),
    ).toBeInTheDocument();

    expect(sent(requests, "PUT", upload).map((request) => request.headers)).toEqual([
      expect.objectContaining({
        "content-type": "application/octet-stream",
        "x-ddt-reauthentication": "linux-agent-proof",
      }),
    ]);
    expect(sent(requests, "PUT", "/api/settings/agent/binary")).toEqual([]);
    expect(reads(requests, "/api/settings/agent/linux")).toBe(1);

    await expectAccessible();
  });

  it("removes the uploaded agent for Linux and shows the one the server came with", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "GET /api/settings/agent/linux": () =>
          json(
            agentView({
              sha256,
              size: 19_534_304,
              uploadedUtc: new Date().toISOString(),
              uploadedBy: "admin",
              source: "Uploaded",
              version: "26.1.400",
              newerBundledVersion: "26.1.512",
            }),
          ),
        "DELETE /api/settings/agent/linux/binary": () =>
          json(agentView({ sha256, size: 19_521_984, source: "Bundled", version: "26.1.512" })),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText(
        "This server came with agent 26.1.512, which is newer than the uploaded one.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remove the uploaded agent for Linux" }));
    const confirm = await screen.findByRole("dialog", {
      name: "Remove the uploaded agent for Linux?",
    });
    fireEvent.click(
      within(confirm).getByRole("button", { name: "Remove the uploaded agent for Linux" }),
    );

    expect(
      await screen.findByText("The agent for Linux this server came with"),
    ).toBeInTheDocument();
    expect(screen.getByText("Removed.")).toBeInTheDocument();
    expect(sent(requests, "DELETE", upload)).toHaveLength(1);
    expect(reads(requests, "/api/settings/agent/linux")).toBe(1);
  });

  it("offers no upload while configuration names the agent for Linux, and says where", async () => {
    serveServer(
      {
        "GET /api/settings": () =>
          json(
            overview({
              server: [
                {
                  key: "DDT:Agent:LinuxBinaryPath",
                  value: null,
                  isSet: true,
                  source: "command line",
                  secret: false,
                },
              ],
            }),
          ),
        "GET /api/settings/agent": () => json(agentView()),
        "GET /api/settings/agent/linux": () =>
          json(agentView({ sha256, size: 19_521_984, source: "Configuration" })),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The file DDT:Agent:LinuxBinaryPath names in configuration"),
    ).toBeInTheDocument();
    expect(await screen.findByText("Uploads are off")).toBeInTheDocument();
    expect(
      screen.getByText(/DDT:Agent:LinuxBinaryPath names the agent for Linux in configuration/),
    ).toBeInTheDocument();
    expect(await screen.findByText("The key is set from the command line.")).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Upload the agent for Linux" }),
    ).not.toBeInTheDocument();

    // The agent for Windows PE beside it still takes an upload.
    expect(screen.getByRole("heading", { name: "Upload the agent" })).toBeInTheDocument();
  });

  it("shows what machines run when the server has no agent for Linux", async () => {
    serveServer({ "GET /api/settings/agent": () => json(agentView()) }, { tab: "agent" });

    expect(
      await screen.findByText("The agent the Linux boot environment brings"),
    ).toBeInTheDocument();
    expect(screen.getByText("Drop ddt-agent-linux-x64 here, or choose it.")).toBeInTheDocument();
    expect(
      screen.getByText("A 64-bit Linux program for x64 of at most 128 MB."),
    ).toBeInTheDocument();
  });
});
