// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { proof, typePassword } from "@/settings/serverTesting";
import { chooseOption, nth, press, selectKey } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator, bootImageView, operator } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { rowOf } from "@/test/rowOf";
import { json, type Routes } from "@/test/server";

import type { BootImageView } from "./bootImage";
import type { BootImageJob } from "./bootImageJob";

const started = "2026-10-02T08:00:00Z";

function job(overrides: Partial<BootImageJob> = {}): BootImageJob {
  return {
    kind: "Build",
    state: "Running",
    startedUtc: started,
    finishedUtc: null,
    startedBy: "admin",
    problem: null,
    lines: 1,
    ...overrides,
  };
}

function open(view: BootImageView, routes: Routes = {}, user = administrator) {
  return renderPage({
    path: "/boot/image",
    user,
    routes: { "GET /api/boot-image": { body: view }, ...routes },
  });
}

describe("BootImagePage", () => {
  it("builds with the chosen options and follows the job over the hub without reading the page again", async () => {
    const { server, hub } = await open(bootImageView(), {
      "POST /api/boot-image/build": { status: 202 },
      "GET /api/boot-image/job": { body: { job: job(), lines: ["Copying Windows PE"] } },
    });

    const panel = rowOf(
      await screen.findByRole("heading", { name: "Build on this server" }),
      "section",
    );
    expect(selectKey(panel, "Keyboard layout")).toHaveTextContent("German");
    expect(within(panel).getByText(/10\.1\.26100\.9457/)).toBeInTheDocument();
    await expectNoAxeViolations();

    await chooseOption(panel, "Keyboard layout", "English (United States)");
    press(within(panel).getByRole("checkbox", { name: /With PowerShell/ }));
    press(within(panel).getByRole("button", { name: "Build the boot image" }));

    await waitFor(() => {
      expect(server.changes().map((request) => request.body)).toEqual([
        { keyboardLayout: "0409:00000409", skipPowerShell: true },
      ]);
    });

    act(() => {
      hub?.push("bootImageChanged", bootImageView({ job: job() }));
    });

    const output = await screen.findByRole("log", { name: "Output" });
    expect(output).toHaveTextContent("Copying Windows PE");
    expect(screen.getByText("Running")).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Build the boot image" })).toBeDisabled();

    act(() => {
      hub?.push("bootImageJobOutput", {
        startedUtc: started,
        first: 1,
        lines: ["Adding the agent"],
      });
    });
    await waitFor(() => {
      expect(output).toHaveTextContent("Copying Windows PE Adding the agent");
    });
    expect(server.count("GET /api/boot-image/job")).toBe(1);

    // A batch that does not continue what the page holds means lines were missed
    server.routes["GET /api/boot-image/job"] = {
      body: { job: job({ lines: 4 }), lines: ["Copying Windows PE", "Adding the agent", "3", "4"] },
    };
    act(() => {
      hub?.push("bootImageJobOutput", { startedUtc: started, first: 3, lines: ["4"] });
    });
    await waitFor(() => {
      expect(output).toHaveTextContent("Adding the agent 3 4");
    });

    act(() => {
      hub?.push(
        "bootImageChanged",
        bootImageView({
          job: job({ state: "Succeeded", finishedUtc: "2026-10-02T08:09:00Z", lines: 4 }),
          builds: [
            { name: "20261002-080000", builtUtc: "2026-10-02T08:09:00Z", current: true },
            { name: "20261001-100000", builtUtc: "2026-10-01T10:00:00Z", current: false },
          ],
        }),
      );
    });

    expect(await screen.findByText("Done")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Builds on the server" })).toBeInTheDocument();
    expect(server.count("GET /api/boot-image")).toBe(1);
    expect(server.count("GET /api/boot-image/job")).toBe(2);
  });

  it("offers to install the ADK on a server without one, and says why a job did not start", async () => {
    const { server } = await open(
      bootImageView({
        build: null,
        builds: [],
        builder: {
          available: true,
          serverUrl: "https://deploy01.contoso.local:8443",
          adk: { installed: false, version: null, supported: false },
          package: false,
        },
      }),
      {
        "POST /api/boot-image/adk": () =>
          json({ title: "A build or an install of the ADK is running already." }, 409),
      },
    );

    expect(await screen.findByText(/this server has none/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Build the boot image" })).toBeDisabled();
    expect(selectKey(document.body, "Keyboard layout")).toHaveTextContent("The server's own");

    press(screen.getByRole("button", { name: "Install the Windows ADK" }));
    const dialog = await screen.findByRole("dialog", { name: "Install the Windows ADK?" });
    expect(dialog).toHaveTextContent("Microsoft's licence terms");
    expect(server.changes()).toEqual([]);

    press(within(dialog).getByRole("button", { name: "Install" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("running already");
    expect(server.count("POST /api/boot-image/adk")).toBe(1);
  });

  it("gives the command for a Windows PC when the server cannot build", async () => {
    await open(
      bootImageView({
        builder: {
          available: false,
          serverUrl: "https://ddt.example:8443",
          adk: null,
          package: false,
        },
      }),
    );

    expect(await screen.findByLabelText("Build command")).toHaveTextContent(
      "-ServerUrl https://ddt.example:8443",
    );
    expect(screen.queryByRole("button", { name: "Build the boot image" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Builds on the server" })).not.toBeInTheDocument();
  });

  it("hands out the builder for another PC after the password, where the server cannot build", async () => {
    const saved: string[] = [];
    vi.stubGlobal("URL", {
      ...URL,
      createObjectURL: () => "blob:builder",
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      saved.push(`${this.download} from ${this.href}`);
    });

    const { server } = await open(
      bootImageView({
        builder: {
          available: false,
          serverUrl: "https://ddt.example:8443",
          adk: null,
          package: true,
        },
        job: job({ kind: "Upload", state: "Succeeded", startedBy: "admin" }),
      }),
      {
        "POST /api/settings/reauthenticate": () => proof("builder-proof"),
        "POST /api/boot-image/builder": () => new Response("zip", { status: 200 }),
        "GET /api/boot-image/job": { body: { job: job({ kind: "Upload" }), lines: [] } },
      },
    );

    const panel = rowOf(
      await screen.findByRole("heading", { name: "Build on another PC" }),
      "section",
    );
    expect(screen.queryByLabelText("Build command")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Boot image from a builder" })).toBeInTheDocument();

    press(within(panel).getByRole("button", { name: "Download the builder" }));
    expect(server.count("POST /api/boot-image/builder")).toBe(0);
    await typePassword();

    expect(
      await within(panel).findByText(/Saved as ddt-boot-image-builder\.zip/),
    ).toBeInTheDocument();
    expect(saved).toEqual(["ddt-boot-image-builder.zip from blob:builder"]);
    expect(
      nth(
        server.requests.filter((request) => request.path === "/api/boot-image/builder"),
        0,
      ).headers.get("X-DDT-Reauthentication"),
    ).toBe("builder-proof");
  });

  it("says each reason the image has to be built again, and goes back to the build before", async () => {
    const stale = bootImageView({
      stale: true,
      staleReasons: ["serverAddress", "root", "adk"],
      builder: {
        available: true,
        serverUrl: "https://deploy02.contoso.local:8443",
        adk: { installed: true, version: "10.1.26100.9999", supported: true },
        package: true,
      },
      builds: [
        { name: "20261001-100000", builtUtc: "2026-10-01T10:00:00Z", current: true },
        { name: null, builtUtc: "2026-09-01T10:00:00Z", current: false },
      ],
    });
    const back = {
      ...stale,
      builds: stale.builds.map((build) => ({ ...build, current: !build.current })),
    };
    const { server } = await open(stale, { "POST /api/boot-image/current": { body: back } });

    const notice = rowOf(
      await screen.findByText("The boot image has to be built again"),
      "[role=status]",
    );
    expect(notice).toHaveTextContent("It names the server https://deploy01.contoso.local:8443");
    expect(notice).toHaveTextContent("It trusts another root certificate");
    expect(notice).toHaveTextContent("10.1.26100.9457, and the server now has 10.1.26100.9999");

    const builds = rowOf(screen.getByRole("heading", { name: "Builds on the server" }), "section");
    const rows = within(builds).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("Served");
    expect(rows[1]).toHaveTextContent("Copied into the boot directory by hand");

    press(within(builds).getByRole("button", { name: "Serve this build" }));
    const dialog = await screen.findByRole("dialog", { name: "Serve this build?" });
    press(within(dialog).getByRole("button", { name: "Serve this build" }));

    await waitFor(() => {
      expect(within(builds).getAllByRole("listitem")[1]).toHaveTextContent("Served");
    });
    expect(server.changes().map((request) => request.body)).toEqual([{ name: null }]);
    expect(server.count("GET /api/boot-image")).toBe(1);
  });

  it("shows an operator the state and a failed job, but nothing to start", async () => {
    await open(
      bootImageView({
        job: job({ state: "Failed", problem: "Build-BootImage.ps1 ended with exit code 1." }),
        builds: [
          { name: "20261001-100000", builtUtc: "2026-10-01T10:00:00Z", current: true },
          { name: "20260901-100000", builtUtc: "2026-09-01T10:00:00Z", current: false },
        ],
      }),
      {
        "GET /api/boot-image/job": { body: { job: job({ state: "Failed" }), lines: ["dism: 5"] } },
      },
      operator,
    );

    expect(
      await screen.findByText("Only administrators build the boot image."),
    ).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("ended with exit code 1");
    expect(await screen.findByRole("log", { name: "Output" })).toHaveTextContent("dism: 5");
    expect(screen.queryByRole("button", { name: "Build the boot image" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Serve this build" })).not.toBeInTheDocument();
  });
});
