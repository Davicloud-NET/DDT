// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { toasts } from "@/ui/toasts";

import { administrator, field, opened, saveWait, serve, view } from "./SequenceEditorPage.fixtures";
import type { RunScriptStep, SequenceProblem, SequenceStep, SequenceView } from "./sequences";
import { newStep } from "./steps";

const shell: RunScriptStep = {
  ...(newStep("runScript", "s", "Linux") as RunScriptStep),
  name: "Before the disk",
  script: 'echo "$DDT_PHASE"',
};

// As a server before version 4 stored it, for Windows PE.
const forWindowsPE: RunScriptStep = {
  ...(newStep("runScript", "s") as RunScriptStep),
  name: "Before the disk",
  script: "echo %DDT_PHASE%",
  rebootExitCodes: [],
};

const rewrite: SequenceProblem = {
  stepId: "s",
  field: "phase",
  message:
    "This script was written for Windows PE, which no longer runs Linux sequences. Rewrite it for the shell and let it run in the Linux boot environment or in the installed Linux.",
  code: "sequence.windowsScriptInLinux",
};

// What the server answers for such a sequence: the script's phase is the one it was stored with.
const stored: Partial<SequenceView> = {
  stepPhases: ["WindowsPE", "LinuxBoot", "LinuxBoot"],
  problems: [rewrite],
};

// A sequence that writes a raw disk image, with a script before it.
function linuxView(script: SequenceStep, overrides: Partial<SequenceView> = {}): SequenceView {
  return view({
    name: "Install Linux",
    definition: {
      version: 4,
      steps: [script, newStep("writeRawImage", "w"), newStep("writeCloudInitSeed", "c")],
    },
    stepPhases: ["LinuxBoot", "LinuxBoot", "LinuxBoot"],
    ...overrides,
  });
}

async function optionsOf(name: string): Promise<(string | null)[]> {
  press(within(field(name)).getByRole("button"));
  const options = await screen.findAllByRole("option");
  const labels = options.map((option) => option.textContent);

  press(options[0] ?? document.body);
  await waitFor(() => {
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  return labels;
}

describe("SequenceEditorPage, a Linux sequence", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("offers a script both Linux phases and the shell, and in the boot environment nothing that it cannot do", async () => {
    serve(administrator, linuxView(shell), { step: "s" });

    await opened("Install Linux");

    expect(screen.getByText(/^Runs as root\./)).toBeInTheDocument();
    expect(screen.getByText(/^Started with \/bin\/sh/)).toBeInTheDocument();
    expect(await optionsOf("phase")).toEqual([
      "Linux boot environment, before the hand-over",
      "Installed Linux, after the hand-over",
    ]);
    expect(await optionsOf("interpreter")).toEqual(["Shell"]);
    expect(document.querySelector('[data-field="packageId"]')).toBeNull();
    expect(document.querySelector('[data-field="rebootExitCodes"]')).toBeNull();
    expect(document.querySelector('[data-field="runAs"]')).toBeNull();
    expect(screen.queryByText("Network shares")).not.toBeInTheDocument();
    expect(within(field("successExitCodes")).getByText(/from 0 to 255/)).toBeInTheDocument();
  });

  it("starts a new script as a shell script in the Linux boot environment that asks for no restart", async () => {
    const { saves } = serve(administrator, linuxView(shell), { step: "w" });

    await opened("Install Linux");
    press(
      screen.getByRole("button", {
        name: "Add a step between Write raw disk image and Write the cloud-init seed",
      }),
    );
    press(within(await screen.findByRole("menu")).getByRole("menuitem", { name: "Run script" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        kind: "runScript",
        phase: "LinuxBoot",
        interpreter: "Shell",
        rebootExitCodes: [],
      });
    }, saveWait);
  });

  // The script keeps what it was stored with, next to the problem that says what to change.
  it("shows a script written for Windows PE as it is, and makes it a shell script with the Linux boot environment", async () => {
    const { saves } = serve(administrator, linuxView(forWindowsPE, stored), { step: "s" });

    await opened("Install Linux");

    expect(within(field("phase")).getByRole("button")).toHaveTextContent(
      "Windows PE, before the hand-over",
    );
    expect(within(field("phase")).getByRole("button")).toHaveAccessibleDescription(
      /This script was written for Windows PE/,
    );
    expect(within(field("interpreter")).getByRole("button")).toHaveTextContent("cmd");

    press(within(field("phase")).getByRole("button"));
    press(
      await screen.findByRole("option", { name: "Linux boot environment, before the hand-over" }),
    );

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[0]).toMatchObject({
        phase: "LinuxBoot",
        interpreter: "Shell",
      });
    }, saveWait);
  });

  it("says of a restart that the Linux boot environment cannot restart during the run", async () => {
    const restart = { ...newStep("reboot", "r"), name: "Restart" };
    const definition = { version: 4, steps: [newStep("writeRawImage", "w"), restart] };

    serve(administrator, linuxView(shell, { definition, stepPhases: ["LinuxBoot", "LinuxBoot"] }), {
      step: "r",
    });

    await opened("Install Linux");

    expect(
      screen.getByText("A restart is only possible in the installed Linux."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/In Windows PE this needs a partitioned disk/)).toBeNull();
  });

  it("draws no hand-over line under a script that was stored for Windows PE", async () => {
    serve(administrator, linuxView(forWindowsPE, stored));

    await opened("Install Linux");

    expect(
      screen.getByRole("button", {
        name: "Add a step between Before the disk and Write raw disk image",
      }),
    ).toBeInTheDocument();
    expect(screen.queryByText("In Windows PE")).not.toBeInTheDocument();
    expect(screen.queryByText(/after the hand-over$/)).not.toBeInTheDocument();
  });
});
