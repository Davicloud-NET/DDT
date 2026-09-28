// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { sequenceResolution, viewer } from "@/test/builders";

import {
  answers,
  machine,
  machineId,
  open,
  planned,
  run,
  step,
  stepRow,
  view,
} from "./MachinePage.fixtures";

describe("MachinePage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("reads again what chooses the sequence once the machine's run changes", async () => {
    const resolutionCall = `GET /api/machines/${machineId}/sequence`;
    const routes = answers([machine({ state: "Pending", deployment: null })], [], []);
    routes[resolutionCall] = {
      body: sequenceResolution({
        source: "ModelRule",
        sequenceId: "0193a4b2-0000-7000-8000-0000000000e1",
        sequenceName: "Install Windows",
        ruleId: "0193a4b2-0000-7000-8000-0000000000f1",
        explanation: "The rule for model Virtual Machine chooses Install Windows.",
      }),
    };
    const { server, hub } = await open(routes);

    expect(
      await screen.findByText("The rule for model Virtual Machine chooses Install Windows."),
    ).toBeInTheDocument();

    routes[resolutionCall] = {
      body: sequenceResolution({
        source: "Assigned",
        explanation: "operator approved PC-042 with Install Windows, which a rule chose.",
      }),
    };
    act(() => {
      hub?.push(
        "machineChanged",
        machine({
          state: "Done",
          deployment: run({
            state: "Done",
            source: "Rule",
            activity: null,
            finishedUtc: "2026-09-16T10:30:00Z",
          }),
        }),
      );
    });

    expect(
      await screen.findByText("operator approved PC-042 with Install Windows, which a rule chose."),
    ).toBeInTheDocument();
    expect(server.count(resolutionCall)).toBe(2);
  });

  it.each([
    {
      phase: "WindowsPE" as const,
      consequence:
        "This stops the run on PC-042. Its disk is left half written; assign a sequence again to deploy it.",
    },
    {
      phase: "Windows" as const,
      consequence:
        "This stops the run on PC-042, which runs in its installed Windows. The agent there stops at its next contact with the server and removes itself; Windows stays installed as it is, with the steps done so far.",
    },
  ])("says what stopping a run in $phase leaves behind", async ({ phase, consequence }) => {
    const current = run({ phase });
    await open(
      answers([machine({ deployment: current })], [current], [view({ summary: current })]),
    );

    press(await screen.findByRole("button", { name: "Stop the run" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the run on PC-042?" });

    expect(dialog).toHaveAccessibleDescription(consequence);
  });

  it("stops the run and shows how it ended without reading the machines again", async () => {
    const stopped = run({
      state: "Failed",
      activity: null,
      finishedUtc: "2026-09-16T10:06:00Z",
      updatedUtc: "2026-09-16T10:06:00Z",
      error: "Stopped by operator.",
    });
    const routes = answers([machine()], [run()], [view()]);
    routes[`DELETE /api/machines/${machineId}/deployments/current`] = {
      body: machine({ state: "Failed", deployment: stopped }),
    };
    const { server } = await open(routes);

    press(await screen.findByRole("button", { name: "Stop the run" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the run on PC-042?" });
    press(within(dialog).getByRole("button", { name: "Stop the run" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(await screen.findByRole("alert")).toHaveTextContent("Stopped by operator.");
    expect(screen.queryByRole("button", { name: "Stop the run" })).not.toBeInTheDocument();
    expect(server.count("GET /api/machines")).toBe(1);
  });

  it("says a run from before task sequences has no steps", async () => {
    const legacy = run({
      sequenceId: null,
      title: "Windows 11 Pro",
      state: "Done",
      stepCount: 0,
      stepIndex: null,
      stepName: null,
      activity: null,
      finishedUtc: "2026-09-16T10:30:00Z",
    });

    await open(
      answers(
        [machine({ state: "Done", deployment: legacy })],
        [legacy],
        [view({ summary: legacy, definition: null, steps: [] })],
      ),
    );

    expect(
      await screen.findByText(
        "This deployment ran before task sequences, so it has no steps to show.",
      ),
    ).toBeInTheDocument();
  });

  it("explains that a machine without runs has none yet", async () => {
    await open(answers([machine({ state: "Approved", deployment: null })], [], []));

    expect(await screen.findByText("No runs yet")).toBeInTheDocument();
    expect(
      screen.getByText(
        "Assign a sequence above, or add a rule that chooses one for machines like it under Deployment, Rules.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 2, name: "Install Windows" })).toBeNull();
  });

  it("shows the machine's Secure Boot state, and that its run may write an image not signed for it", async () => {
    const linux = run({
      title: "Install Linux",
      state: "Done",
      stepCount: 2,
      stepIndex: null,
      stepName: null,
      activity: null,
      finishedUtc: "2026-09-16T10:30:00Z",
    });

    await open(
      answers(
        [machine({ state: "Done", deployment: linux, secureBootEnabled: true })],
        [linux],
        [
          view({
            summary: linux,
            definition: {
              version: 2,
              steps: [
                planned("w1", "Write the disk", "writeRawImage"),
                planned("c1", "Seed", "writeCloudInitSeed"),
              ],
            },
            steps: [
              step(0, "w1", "Write the disk", "writeRawImage", { state: "Done" }),
              step(1, "c1", "Seed", "writeCloudInitSeed", { state: "Done" }),
            ],
            artifacts: [
              {
                stepId: "w1",
                kind: "Image",
                sourceId: "0193a4b2-0000-7000-8000-0000000000a9",
                name: "noble",
                sha256: "00",
                sizeBytes: 1,
              },
            ],
            allowSecureBootMismatch: true,
          }),
        ],
      ),
    );

    expect(
      await screen.findByText(
        "Allowed to write noble although it may not start with Secure Boot on.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Secure Boot", { selector: "dt" }).nextElementSibling,
    ).toHaveTextContent("On");
    expect(
      within(stepRow("Write the disk")).getByText("Write raw disk image, Windows PE"),
    ).toBeInTheDocument();
    expect(
      within(stepRow("Seed")).getByText("Write the cloud-init seed, Windows PE"),
    ).toBeInTheDocument();
  });

  it("says when the machine was removed, also while its page is open", async () => {
    const { hub } = await open(answers([machine()], [run()], [view()]));

    await screen.findByRole("heading", { level: 1, name: "PC-042" });

    act(() => {
      hub?.push("machinesRemoved", { machineIds: [machineId] });
    });

    expect(await screen.findByText("This machine was removed")).toBeInTheDocument();
    expect(
      screen.getByText("It registers as a new machine at its next netboot."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Steps" })).not.toBeInTheDocument();
  });

  it("offers no actions to viewers", async () => {
    await open(answers([machine({ state: "Pending", deployment: null })], [], []), {
      user: viewer,
    });

    await screen.findByRole("heading", { level: 1, name: "PC-042" });
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^More for/ })).not.toBeInTheDocument();
  });
});
