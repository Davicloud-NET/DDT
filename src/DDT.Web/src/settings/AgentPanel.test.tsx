// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
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

const sha256 = "3f7a9c0e5b1d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f0a2c4e6b8d0f2a";

function agentFile(name = "ddt-agent.exe", bytes = [0x4d, 0x5a, 0x90, 0x00]): File {
  return new File([new Uint8Array(bytes)], name, { type: "application/x-msdownload" });
}

async function choose(file: File, panel = "Upload the agent"): Promise<void> {
  const input = (await screen.findByRole("heading", { name: panel }))
    .closest("section")
    ?.querySelector('input[type="file"]');

  if (!input) {
    throw new Error("The upload has no file input.");
  }

  fireEvent.change(input, { target: { files: [file] } });
}

describe("AgentPanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // First in this file, so no proof of identity is held yet.
  it("asks for the password before sending the agent, and shows the answer without reading it again", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "POST /api/settings/reauthenticate": () => proof("agent-proof"),
        "PUT /api/settings/agent/binary": () =>
          json(
            agentView({
              sha256,
              size: 11_534_336,
              uploadedUtc: new Date().toISOString(),
              uploadedBy: "admin",
              source: "Uploaded",
            }),
          ),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The agent in their boot image, since none was uploaded"),
    ).toBeInTheDocument();
    expect(screen.getByText(/runs as SYSTEM on every machine that netboots/)).toBeInTheDocument();
    expect(screen.getByText(/does not have to be built again/)).toBeInTheDocument();

    await choose(agentFile());
    const confirm = await screen.findByRole("dialog", {
      name: "Upload ddt-agent.exe as the agent?",
    });
    fireEvent.click(within(confirm).getByRole("button", { name: "Upload agent" }));

    await screen.findByRole("dialog", { name: "Confirm it is you" });
    expect(sent(requests, "PUT", "/api/settings/agent/binary")).toEqual([]);
    await typePassword();

    expect(
      await screen.findByText(
        `Uploaded. Machines that netboot from now on run the agent with SHA-256 ${sha256}.`,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("The agent uploaded here")).toBeInTheDocument();
    expect(screen.getByText("now by admin")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy SHA-256" })).toBeInTheDocument();

    const puts = sent(requests, "PUT", "/api/settings/agent/binary");
    expect(puts.map((request) => request.headers)).toEqual([
      expect.objectContaining({
        "content-type": "application/octet-stream",
        "x-ddt-reauthentication": "agent-proof",
      }),
    ]);
    expect(reads(requests, "/api/settings/agent")).toBe(1);
  });

  it("offers no upload while configuration names the agent, and says where", async () => {
    serveServer(
      {
        "GET /api/settings": () =>
          json(
            overview({
              server: [
                {
                  key: "DDT:Agent:BinaryPath",
                  value: null,
                  isSet: true,
                  source: "environment variable",
                  secret: false,
                },
              ],
            }),
          ),
        "GET /api/settings/agent": () =>
          json(agentView({ sha256, size: 11_534_336, source: "Configuration" })),
      },
      { tab: "agent" },
    );

    expect(await screen.findByText("Uploads are off")).toBeInTheDocument();
    expect(
      await screen.findByText("The key is set from an environment variable."),
    ).toBeInTheDocument();
    expect(screen.getByText(sha256)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Upload the agent" })).not.toBeInTheDocument();

    await expectAccessible();
  });

  it("says why the server refused the agent while configuration names it", async () => {
    serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "POST /api/settings/reauthenticate": () => proof("agent-proof"),
        "PUT /api/settings/agent/binary": () =>
          json(
            {
              title:
                "DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.",
              status: 409,
            },
            409,
          ),
      },
      { tab: "agent" },
    );

    await choose(agentFile());
    fireEvent.click(
      within(await screen.findByRole("dialog")).getByRole("button", { name: "Upload agent" }),
    );

    // The proof of the first test is still held; without it, the password comes first.
    if (screen.queryByRole("dialog", { name: "Confirm it is you" }) !== null) {
      await typePassword();
    }

    expect(
      await screen.findByText(
        /ddt-agent.exe was not uploaded. DDT:Agent:BinaryPath names the agent in configuration/,
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("refuses an empty file without sending it", async () => {
    const { requests } = serveServer(
      { "GET /api/settings/agent": () => json(agentView()) },
      { tab: "agent" },
    );

    await choose(agentFile("empty.exe", []));

    expect(await screen.findByText("empty.exe is empty.")).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(sent(requests, "PUT", "/api/settings/agent/binary")).toEqual([]);
    await expectAccessible();
  });
});

describe("ConsolePanel", () => {
  const consoleSha256 = "9b1d7e3c5a0f2e4d6b8c0a2e4f6d8b0c2a4e6f8d0b2c4a6e8f0d2b4c6a8e0f2d";

  function consoleZip(): File {
    return new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], "console.zip", {
      type: "application/zip",
    });
  }

  it("uploads the zip of the console beside the agent, and shows what machines show", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "POST /api/settings/reauthenticate": () => proof("console-proof"),
        "PUT /api/settings/agent/console": () =>
          json(
            agentView({
              sha256: consoleSha256,
              size: 30_408_704,
              uploadedUtc: new Date().toISOString(),
              uploadedBy: "admin",
              source: "Uploaded",
            }),
          ),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The console in their boot image, since none was uploaded"),
    ).toBeInTheDocument();

    await choose(consoleZip(), "Upload the console");
    fireEvent.click(
      within(
        await screen.findByRole("dialog", { name: "Upload console.zip as the console?" }),
      ).getByRole("button", { name: "Upload console" }),
    );

    // A proof an earlier test left is still held; without it, the password comes first.
    if (screen.queryByRole("dialog", { name: "Confirm it is you" }) !== null) {
      await typePassword();
    }

    expect(
      await screen.findByText(
        `Uploaded. Machines that netboot from now on show the console whose ddt-console.exe has SHA-256 ${consoleSha256}.`,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("The console uploaded here")).toBeInTheDocument();
    expect(screen.getByText(consoleSha256)).toBeInTheDocument();
    expect(
      sent(requests, "PUT", "/api/settings/agent/console").map((request) => request.headers),
    ).toEqual([expect.objectContaining({ "content-type": "application/zip" })]);
    expect(sent(requests, "PUT", "/api/settings/agent/binary")).toEqual([]);
    expect(reads(requests, "/api/settings/agent/console")).toBe(1);
  });

  it("offers no console upload while configuration names it", async () => {
    serveServer(
      {
        "GET /api/settings/agent": () => json(agentView()),
        "GET /api/settings/agent/console": () =>
          json(agentView({ sha256: consoleSha256, size: 30_408_704, source: "Configuration" })),
      },
      { tab: "agent" },
    );

    expect(
      await screen.findByText("The zip DDT:Agent:ConsolePath names in configuration"),
    ).toBeInTheDocument();
    expect(screen.getByText("Uploads are off")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Upload the agent" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Upload the console" })).not.toBeInTheDocument();

    await expectAccessible();
  });
});
