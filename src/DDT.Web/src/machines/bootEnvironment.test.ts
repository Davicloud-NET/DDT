// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { AgentEnvironment } from "@/deployments/deployments";
import { deploymentSummary, machineSummary, sequenceSummary } from "@/test/builders";

import {
  environmentFacts,
  environmentLine,
  environmentName,
  startNotice,
  startsIn,
} from "./bootEnvironment";

const both: AgentEnvironment[] = ["WindowsPE", "LinuxBoot"];
const windows = sequenceSummary({ name: "Install Windows" });
const linux = sequenceSummary({ name: "Install Linux", kind: "Linux" });

describe("the boot environment of a machine", () => {
  it("names each environment an agent registers from", () => {
    expect(
      (["WindowsPE", "LinuxBoot", "Windows"] as const).map((environment) =>
        environmentName(environment),
      ),
    ).toEqual(["Windows PE", "Linux boot environment", "Installed Windows"]);
  });

  it("starts a sequence that writes a raw disk image in the Linux boot environment", () => {
    expect([startsIn(windows), startsIn(linux)]).toEqual(["WindowsPE", "LinuxBoot"]);
  });

  it("says in a row what the machine started, and where it restarts into while it was asked to", () => {
    expect(environmentLine(machineSummary({ agentEnvironment: "WindowsPE" }))).toBe(
      "Started Windows PE",
    );
    expect(environmentLine(machineSummary({ agentEnvironment: "LinuxBoot" }))).toBe(
      "Started the Linux boot environment",
    );
    expect(
      environmentLine(
        machineSummary({ agentEnvironment: "WindowsPE", nextEnvironment: "LinuxBoot" }),
      ),
    ).toBe("Restarts into the Linux boot environment");
    expect(
      environmentLine(
        machineSummary({ agentEnvironment: "LinuxBoot", nextEnvironment: "WindowsPE" }),
      ),
    ).toBe("Restarts into Windows PE");
  });

  it("says nothing in a row about the installed Windows or for a server that names no environment", () => {
    expect(environmentLine(machineSummary({ agentEnvironment: "Windows" }))).toBeNull();
    expect(environmentLine(machineSummary())).toBeNull();
  });

  it("lists the environment as a fact, and the one the machine restarts into only while it was asked to", () => {
    expect(environmentFacts(machineSummary({ agentEnvironment: "LinuxBoot" }))).toEqual([
      { label: "Environment", value: "Linux boot environment" },
    ]);
    expect(
      environmentFacts(
        machineSummary({ agentEnvironment: "WindowsPE", nextEnvironment: "LinuxBoot" }),
      ),
    ).toEqual([
      { label: "Environment", value: "Windows PE" },
      { label: "Restarts into", value: "Linux boot environment" },
    ]);
    expect(environmentFacts(machineSummary({ agentEnvironment: "Windows" }))).toEqual([
      { label: "Environment", value: "Installed Windows" },
    ]);
    expect(environmentFacts(machineSummary())).toEqual([]);
  });
});

describe("what assigning says about where a sequence starts", () => {
  const inWindowsPE = machineSummary({ agentEnvironment: "WindowsPE" });
  const inLinuxBoot = machineSummary({ agentEnvironment: "LinuxBoot" });

  it("says nothing for a machine that waits where the sequence starts", () => {
    expect(startNotice(inWindowsPE, windows, both)).toBeNull();
    expect(startNotice(inLinuxBoot, linux, both)).toBeNull();
    // The machine is there already, whatever the server could start.
    expect(startNotice(inWindowsPE, windows, [])).toBeNull();
  });

  it("says that a machine in the other boot environment restarts first", () => {
    expect(startNotice(inWindowsPE, linux, both)).toEqual({
      warning: false,
      text: "Install Linux runs in the Linux boot environment, and this machine started Windows PE. The machine restarts into the Linux boot environment first.",
    });
    expect(startNotice(inLinuxBoot, windows, both)).toEqual({
      warning: false,
      text: "Install Windows runs in Windows PE, and this machine started the Linux boot environment. The machine restarts into Windows PE first.",
    });
  });

  it("warns when the server cannot start the boot environment the sequence runs in", () => {
    expect(startNotice(inWindowsPE, linux, ["WindowsPE"])).toEqual({
      warning: true,
      text: "Install Linux runs in the Linux boot environment, which this server does not have. The run stays assigned and does not start until the server has it.",
    });
    expect(startNotice(inLinuxBoot, windows, ["LinuxBoot"])).toEqual({
      warning: true,
      text: "Install Windows runs in Windows PE, and this server has no boot image to start it from. The run stays assigned and does not start until an administrator builds one on the Boot image page.",
    });
  });

  it("leaves a machine that is not waiting in a boot environment to its next start from the network", () => {
    const done = machineSummary({ state: "Done", agentEnvironment: "WindowsPE" });
    const running = machineSummary({
      state: "Failed",
      agentEnvironment: "WindowsPE",
      deployment: deploymentSummary({ state: "Running" }),
    });

    expect(startNotice(done, linux, both)).toBeNull();
    expect(startNotice(running, linux, both)).toBeNull();
    expect(startNotice(done, linux, ["WindowsPE"])).toBeNull();
  });

  it("says nothing before a sequence is chosen, or for a server that names no environments", () => {
    expect(startNotice(inWindowsPE, null, both)).toBeNull();
    expect(startNotice(inWindowsPE, linux, undefined)).toBeNull();
    expect(startNotice(inWindowsPE, linux, null)).toBeNull();
    expect(startNotice(machineSummary(), linux, both)).toBeNull();
  });
});
