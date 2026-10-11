// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { toasts } from "@/ui/toasts";

import { administrator, field, opened, saveWait, serve, view } from "./SequenceEditorPage.fixtures";
import type { RunScriptStep, SequencePhase, SequenceView } from "./sequences";
import { newStep } from "./steps";

const inBoot = "Linux boot environment, before the hand-over";
const installed = "Installed Linux, after the hand-over";

function script(phase: SequencePhase, rebootExitCodes: number[]): RunScriptStep {
  return {
    ...(newStep("runScript", "s", "Linux") as RunScriptStep),
    name: "Install the tools",
    script: "apt-get -y install htop",
    phase,
    rebootExitCodes,
  };
}

// A sequence that writes a raw disk image and its seed, with a script after them.
function linuxView(step: RunScriptStep, overrides: Partial<SequenceView> = {}): SequenceView {
  return view({
    name: "Install Linux",
    definition: {
      version: step.phase === "Linux" ? 5 : 4,
      steps: [newStep("writeRawImage", "w"), newStep("writeCloudInitSeed", "c"), step],
    },
    stepPhases: ["LinuxBoot", "LinuxBoot", step.phase],
    ...overrides,
  });
}

async function choosePhase(name: string) {
  press(within(field("phase")).getByRole("button"));
  press(await screen.findByRole("option", { name }));
}

describe("SequenceEditorPage, a step in the installed Linux", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("offers a script there a package, restart codes and shares, and no account to run as", async () => {
    serve(administrator, linuxView(script("Linux", [194])), { step: "s" });

    await opened("Install Linux");

    expect(screen.getByText(/^Runs as root in the installed Linux/)).toBeInTheDocument();
    expect(within(field("phase")).getByRole("button")).toHaveTextContent(installed);
    expect(field("packageId")).toBeInTheDocument();
    expect(within(field("rebootExitCodes")).getByRole("textbox")).toHaveValue("194");
    expect(
      within(field("rebootExitCodes")).getByText(/A shell makes 194 of exit 3010\.$/),
    ).toBeInTheDocument();
    expect(screen.getByText("Network shares")).toBeInTheDocument();
    expect(document.querySelector('[data-field="runAs"]')).toBeNull();
  });

  it("draws the hand-over line where the installed Linux begins", async () => {
    serve(administrator, linuxView(script("Linux", [194])));

    await opened("Install Linux");

    expect(screen.getByText("In the Linux boot environment")).toBeInTheDocument();
    expect(screen.getByText("In the installed Linux, after the hand-over")).toBeInTheDocument();
    expect(screen.queryByText("In Windows PE")).not.toBeInTheDocument();
  });

  // The server reads the seed's user-data for what keeps the agent out of the installed Linux, and for a restart.
  it("shows what the user-data does to the run: a warning in its colour and a note as a hint", async () => {
    const finding = (code: string, args: Record<string, string>, message: string) => ({
      stepId: "c",
      field: "userData",
      message,
      code,
      args,
    });
    const warnings = [
      finding("sequence.userDataFinalModulesWarning", { how: "missing" }, "Final modules."),
    ];
    const notes = [finding("sequence.userDataPowerStateNote", { mode: "reboot" }, "Power state.")];

    serve(administrator, linuxView(script("Linux", [194]), { warnings, notes }), { step: "c" });

    await opened("Install Linux");

    const userData = within(field("userData"));
    const warning = userData.getByText(
      /^The user-data sets cloud_final_modules, and the list names/,
    );
    const note = userData.getByText(
      "The user-data restarts the machine with power_state. The run waits for that restart and then goes on with its steps in the installed Linux.",
    );

    expect(warning).toHaveClass("text-attention-text");
    expect(note).not.toHaveClass("text-attention-text");
    expect(userData.getByText(/^A #cloud-config document/)).toBeInTheDocument();
    // Only the warning is counted.
    expect(screen.getAllByText("1 warning").length).toBeGreaterThan(0);
    expect(screen.queryByText(/2 warnings/)).not.toBeInTheDocument();
  });

  // Before the seed it starts in the Linux boot environment, which the test of a Linux sequence holds.
  it("starts a new script after the seed in the installed Linux, with 194 to ask for a restart", async () => {
    const boot = script("LinuxBoot", []);
    const { saves } = serve(
      administrator,
      linuxView(boot, {
        definition: {
          version: 4,
          steps: [boot, newStep("writeRawImage", "w"), newStep("writeCloudInitSeed", "c")],
        },
        stepPhases: ["LinuxBoot", "LinuxBoot", "LinuxBoot"],
      }),
      { step: "c" },
    );

    await opened("Install Linux");
    press(screen.getByRole("button", { name: "Add a step after Write the cloud-init seed" }));
    press(within(await screen.findByRole("menu")).getByRole("menuitem", { name: "Run script" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[3]).toMatchObject({
        kind: "runScript",
        phase: "Linux",
        interpreter: "Shell",
        rebootExitCodes: [194],
      });
    }, saveWait);
    expect(saves.at(-1)?.definition.version).toBe(5);
  });

  it("gives a script 194 to ask for a restart with when it moves to the installed Linux", async () => {
    const { saves } = serve(administrator, linuxView(script("LinuxBoot", [])), { step: "s" });

    await opened("Install Linux");
    await choosePhase(installed);

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        phase: "Linux",
        interpreter: "Shell",
        rebootExitCodes: [194],
      });
    }, saveWait);
  });

  it("takes the restart code away when the script moves back to the Linux boot environment", async () => {
    const { saves } = serve(administrator, linuxView(script("Linux", [194])), { step: "s" });

    await opened("Install Linux");
    await choosePhase(inBoot);

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        phase: "LinuxBoot",
        rebootExitCodes: [],
      });
    }, saveWait);
  });

  // The server then says that the boot environment cannot restart, next to the codes to remove.
  it("keeps restart codes someone entered when the script moves", async () => {
    const { saves } = serve(administrator, linuxView(script("Linux", [194, 200])), { step: "s" });

    await opened("Install Linux");
    await choosePhase(inBoot);

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        phase: "LinuxBoot",
        rebootExitCodes: [194, 200],
      });
    }, saveWait);
  });
});
