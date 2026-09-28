// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { MachineSequenceResolution } from "@/rules/rules";
import type { SequenceSummary } from "@/sequences/sequences";

import { approvalPlan } from "./approval";
import type { MachineSummary } from "./machines";

const machine: MachineSummary = {
  id: "m1",
  state: "Pending",
  smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
  primaryMac: "00155D010203",
  macAddresses: ["00155D010203"],
  manufacturer: "Dell Inc.",
  model: "Latitude 7440",
  serialNumber: null,
  assignedName: "PC-042",
  agentVersion: null,
  firstSeenUtc: "2026-09-16T10:00:00Z",
  lastSeenUtc: "2026-09-16T10:00:00Z",
  lastSeenAddress: null,
  signedInBy: null,
  firstSeenAddress: null,
  everApproved: false,
  disks: null,
  eligibleDiskCount: 1,
  deployment: null,
  secureBootEnabled: null,
  trustedUefiCas: null,
  deviceKind: "Unknown",
};

const chosen: MachineSequenceResolution = {
  source: "MacRule",
  sequenceId: "s1",
  sequenceName: "Install Windows",
  ruleId: "r1",
  problemCount: 0,
  explanation: "The rule for MAC 00:15:5D:01:02:03 chooses Install Windows.",
};

const installWindows: SequenceSummary = {
  id: "s1",
  name: "Install Windows",
  description: null,
  revision: 1,
  stepCount: 5,
  problemCount: 0,
  warningCount: 0,
  erasesDisk: false,
  needsComputerName: false,
  continuesInWindows: true,
  updatedUtc: "2026-09-15T10:00:00Z",
  updatedBy: null,
  rawImageName: null,
  rawImageBootCapability: null,
  rawImageSignedUnder: null,
};

describe("approvalPlan", () => {
  it("needs no confirmation when no rule chooses, or someone at the machine chooses there", () => {
    expect(approvalPlan(machine, { ...chosen, source: "None", sequenceId: null }, [])).toBeNull();
    expect(approvalPlan({ ...machine, signedInBy: "tech" }, chosen, [installWindows])).toBeNull();
  });

  it("runs the rule's sequence and says whether it erases the disk", () => {
    expect(approvalPlan(machine, chosen, [installWindows])).toEqual({
      expectedSequenceId: "s1",
      consequence:
        "Approving PC-042 also runs Install Windows on it, which a rule for its MAC address chose. Its disk is not erased.",
      confirmLabel: "Approve and run Install Windows",
      sequence: installWindows,
      inputs: [],
      defaults: [],
    });
  });

  it("asks the inputs of the rule's sequence that are asked on the web, with what they start with", () => {
    const input = {
      name: "Department",
      label: "Department",
      help: null,
      kind: "Text" as const,
      choices: [],
      default: null,
      required: true,
      maxLength: null,
      account: null,
    };
    const defaults = [
      {
        name: "Department",
        value: "Sales",
        source: "Rule" as const,
        sourceId: "r1",
        sourceName: "Berlin office",
        overridden: false,
      },
    ];
    const plan = approvalPlan(
      machine,
      {
        ...chosen,
        inputs: [
          { ...input, askAt: "Both" },
          { ...input, name: "AssetTag", label: "Asset tag", askAt: "Machine" },
        ],
        inputDefaults: defaults,
      },
      [installWindows],
    );

    expect(plan?.inputs).toEqual([input]);
    expect(plan?.defaults).toEqual(defaults);
  });

  it("says a rule of the ordered list chose the sequence", () => {
    expect(
      approvalPlan(machine, { ...chosen, source: "Rule" }, [installWindows])?.consequence,
    ).toBe(
      "Approving PC-042 also runs Install Windows on it, which a rule chose. Its disk is not erased.",
    );
  });

  it("carries the sequence it runs, and none when it only authorizes the machine", () => {
    const linux = {
      ...installWindows,
      rawImageName: "noble",
      rawImageBootCapability: "NotSigned",
      rawImageSignedUnder: null,
    } as const;

    expect(approvalPlan(machine, chosen, [linux])?.sequence).toBe(linux);
    expect(approvalPlan(machine, { ...chosen, problemCount: 1 }, [linux])?.sequence).toBeNull();
  });

  // A rule's name pattern gives every matching machine a name. So the approval runs the sequence and says which name
  // the machine gets.
  it("runs a sequence that needs a computer name when the machine's values give one", () => {
    const unnamed = { ...machine, assignedName: null };
    const joins = { ...installWindows, needsComputerName: true };
    const named: MachineSequenceResolution = {
      ...chosen,
      values: [
        {
          name: "ComputerName",
          value: "PC-00042",
          source: "Rule",
          sourceId: "r1",
          sourceName: "Office PCs",
          overridden: false,
        },
      ],
    };

    const plan = approvalPlan(unnamed, named, [joins]);

    expect(plan?.expectedSequenceId).toBe("s1");
    expect(plan?.consequence).toBe(
      "Approving Latitude 7440 (00:15:5D:01:02:03) also runs Install Windows on it, which a rule for its MAC address chose. It is named PC-00042. Its disk is not erased.",
    );

    // A name Windows would refuse counts as no name.
    const refused = approvalPlan(
      unnamed,
      {
        ...named,
        valueProblems: [
          { stepId: null, field: "ComputerName", message: "The computer name cannot be used." },
        ],
      },
      [joins],
    );

    expect(refused?.expectedSequenceId).toBeNull();
  });

  it.each([
    {
      where: "the sequence erases a disk and the machine has several",
      target: { ...machine, eligibleDiskCount: 2 },
      sequence: { ...installWindows, erasesDisk: true },
      why: "which erases a disk, and the machine has more than one; sign in at it to choose the disk",
    },
    {
      where: "the sequence joins the domain and the machine has no name",
      target: { ...machine, assignedName: null },
      sequence: { ...installWindows, needsComputerName: true },
      why: "which joins the domain, and the machine has no name yet; assign the sequence with a computer name",
    },
    {
      where: "the sequence names the machine in its seed and the machine has no name",
      target: { ...machine, assignedName: null },
      sequence: { ...installWindows, needsComputerName: true, rawImageName: "noble" },
      why: "which names the machine in its cloud-init seed, and the machine has no name yet; assign the sequence with a computer name",
    },
  ])("only authorizes the machine where $where", ({ target, sequence, why }) => {
    const plan = approvalPlan(target, chosen, [sequence]);

    expect(plan?.expectedSequenceId).toBeNull();
    expect(plan?.consequence).toContain(
      `A rule for its MAC address chooses Install Windows, ${why}.`,
    );
    expect(plan?.confirmLabel).toBe("Approve without a sequence");
  });
});
