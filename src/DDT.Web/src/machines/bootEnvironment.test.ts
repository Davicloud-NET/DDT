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
  restartsFailedLine,
  restartsFailedNotice,
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

describe("a machine the server no longer restarts for its run", () => {
  const assigned = deploymentSummary({ state: "Assigned", title: "Install Linux" });

  it("says in its row which boot environment it did not start", () => {
    expect(
      restartsFailedLine(
        machineSummary({ agentEnvironment: "WindowsPE", restartsFailed: "LinuxBoot" }),
      ),
    ).toBe("Did not start the Linux boot environment after restarting");
    expect(
      restartsFailedLine(
        machineSummary({ agentEnvironment: "LinuxBoot", restartsFailed: "WindowsPE" }),
      ),
    ).toBe("Did not start Windows PE after restarting");
  });

  it("says on its page what to look at and that the run stays assigned", () => {
    expect(
      restartsFailedNotice(
        machineSummary({
          agentEnvironment: "WindowsPE",
          restartsFailed: "LinuxBoot",
          deployment: assigned,
        }),
      ),
    ).toBe(
      "This machine did not start the Linux boot environment after restarting, so DDT no longer restarts it. Check that it starts from the network and that no DHCP server names another boot file. Install Linux stays assigned and starts once the machine starts the Linux boot environment.",
    );
    expect(
      restartsFailedNotice(
        machineSummary({
          agentEnvironment: "LinuxBoot",
          restartsFailed: "WindowsPE",
          deployment: deploymentSummary({ state: "Assigned", title: "Install Windows" }),
        }),
      ),
    ).toBe(
      "This machine did not start Windows PE after restarting, so DDT no longer restarts it. Check that it starts from the network and that no DHCP server names another boot file. Install Windows stays assigned and starts once the machine starts Windows PE.",
    );
  });

  it("says nothing while the server still restarts it, and for a server that does not say", () => {
    const restarting = machineSummary({
      agentEnvironment: "WindowsPE",
      nextEnvironment: "LinuxBoot",
    });

    expect(restartsFailedLine(restarting)).toBeNull();
    expect(restartsFailedNotice(restarting)).toBeNull();
    expect(restartsFailedLine(machineSummary({ restartsFailed: null }))).toBeNull();
    expect(restartsFailedNotice(machineSummary())).toBeNull();
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

  // The server restarts only an agent that can take its sign-in along. An older one gets the run at its next netboot.
  it("warns that an agent too old to change the boot environment is not restarted", () => {
    const old = { changesBootEnvironment: false } as const;

    expect(startNotice({ ...inWindowsPE, ...old }, linux, both)).toEqual({
      warning: true,
      text: "Install Linux runs in the Linux boot environment, and this machine started Windows PE with an agent too old to restart into it. The run stays assigned and starts once the machine is started from the network again.",
    });
    expect(startNotice({ ...inLinuxBoot, ...old }, windows, both)).toEqual({
      warning: true,
      text: "Install Windows runs in Windows PE, and this machine started the Linux boot environment with an agent too old to restart into it. The run stays assigned and starts once the machine is started from the network again.",
    });
    // Where the sequence starts here, the agent's age changes nothing.
    expect(startNotice({ ...inWindowsPE, ...old }, windows, both)).toBeNull();
    // A server that cannot start the environment says that first.
    expect(startNotice({ ...inWindowsPE, ...old }, linux, ["WindowsPE"])?.text).toMatch(
      /which this server does not have/,
    );
  });

  it("says that an agent that can change the boot environment restarts, as a server that does not say", () => {
    expect(
      startNotice({ ...inWindowsPE, changesBootEnvironment: true }, linux, both)?.warning,
    ).toBe(false);
    expect(
      startNotice({ ...inWindowsPE, changesBootEnvironment: null }, linux, both)?.warning,
    ).toBe(false);
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
