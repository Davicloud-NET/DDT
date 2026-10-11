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
