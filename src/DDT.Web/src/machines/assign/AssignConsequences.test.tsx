// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { MachineSummary } from "@/machines/machines";
import type { SequenceSummary } from "@/sequences/sequences";
import { deploymentOptions, machineSummary, sequenceSummary } from "@/test/builders";

import { AssignConsequences } from "./AssignConsequences";

const inWindows = "The run goes on in the installed Windows.";
const inLinux = "The run goes on in the installed Linux.";

function renderConsequences(
  sequence: Partial<SequenceSummary>,
  machine: Partial<MachineSummary> = {},
) {
  render(
    <I18nProvider i18n={i18n}>
      <AssignConsequences
        machine={machineSummary({ agentEnvironment: "LinuxBoot", ...machine })}
        sequence={sequenceSummary({ name: "Install Linux", kind: "Linux", ...sequence })}
        erases
        severalDisks={false}
        risk={null}
        warningId="warning"
        options={deploymentOptions({ bootEnvironments: ["WindowsPE", "LinuxBoot"] })}
        now={Date.now()}
      />
    </I18nProvider>,
  );
}

describe("what assigning a Linux sequence says", () => {
  it("says that the run goes on in the installed Linux when the sequence has steps there", () => {
    renderConsequences({ continuesInLinux: true });

    expect(screen.getByText(inLinux)).toBeInTheDocument();
    expect(screen.queryByText(inWindows)).not.toBeInTheDocument();
  });

  it("says nothing of the installed Linux for a sequence that ends in the boot environment", () => {
    renderConsequences({ continuesInLinux: false });

    expect(screen.queryByText(inLinux)).not.toBeInTheDocument();
    expect(screen.queryByText(inWindows)).not.toBeInTheDocument();
  });

  // The restart starts the server's agent in the Linux boot environment, whatever the agent in Windows PE runs.
  it("restarts a machine in the other boot environment whose agent is too old for the sequence", () => {
    renderConsequences(
      { continuesInLinux: true, version: 5 },
      { agentEnvironment: "WindowsPE", sequenceVersion: 4 },
    );

    expect(
      screen.getByText("The machine restarts into the Linux boot environment first."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/needs a newer agent/)).not.toBeInTheDocument();
  });

  it("says that the run does not start where the machine waits with an agent too old for it", () => {
    renderConsequences({ continuesInLinux: true, version: 5 }, { sequenceVersion: 4 });

    expect(
      screen.getByText(
        "Install Linux needs a newer agent than this machine runs. It starts once the machine is started from the network again.",
      ),
    ).toBeInTheDocument();
  });

  it("restarts a machine whose agent runs the sequence", () => {
    renderConsequences(
      { continuesInLinux: true, version: 5 },
      { agentEnvironment: "WindowsPE", sequenceVersion: 5 },
    );

    expect(
      screen.getByText("The machine restarts into the Linux boot environment first."),
    ).toBeInTheDocument();
  });

  // A run that failed in the installed Linux left the machine there, and the server restarts none from there.
  it("promises no restart of a machine that last registered from the installed Linux", () => {
    renderConsequences({ continuesInLinux: true }, { state: "Failed", agentEnvironment: "Linux" });

    expect(screen.getByText(inLinux)).toBeInTheDocument();
    expect(screen.queryByText(/restarts into/)).not.toBeInTheDocument();
    expect(screen.queryByText(/too old to restart/)).not.toBeInTheDocument();
  });
});
