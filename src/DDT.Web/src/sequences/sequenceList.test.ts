// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import type { RuleView } from "@/rules/rules";
import { deploymentSummary, machineSummary, ruleView } from "@/test/builders";

import {
  activeRunsOf,
  deletionBlocker,
  deletionConsequence,
  findingCounts,
  inFilter,
  matchesSearch,
  removeSummary,
  ruleTarget,
  sequenceFacts,
  summaryOf,
  uniqueName,
  upsertSummary,
  withNewStepIds,
} from "./sequenceList";
import type { SequenceSummary, SequenceView, WriteCloudInitSeedStep } from "./sequences";
import { sequencesSearch, sequenceSearch } from "./sequenceSearch";
import { newStep } from "./steps";

function summary(overrides: Partial<SequenceSummary> = {}): SequenceSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000e1",
    name: "Install Windows",
    description: null,
    revision: 3,
    stepCount: 4,
    problemCount: 0,
    warningCount: 0,
    erasesDisk: true,
    needsComputerName: false,
    continuesInWindows: false,
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
    rawImageName: null,
    rawImageBootCapability: null,
    rawImageSignedUnder: null,
    ...overrides,
  };
}

function view(overrides: Partial<SequenceView> = {}): SequenceView {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000e1",
    name: "Install Windows",
    description: null,
    revision: 4,
    definition: { version: 2, steps: [newStep("partition", "p"), newStep("joinDomain", "j")] },
    stepPhases: ["WindowsPE", "Windows"],
    problems: [{ stepId: "j", field: null, message: "No domain is configured." }],
    warnings: [],
    updatedUtc: "2026-09-16T11:00:00Z",
    updatedBy: "bob",
    ...overrides,
  };
}

function rule(overrides: Partial<RuleView>): RuleView {
  return ruleView({ id: "r1", name: "Latitude laptops", ...overrides });
}

describe("the sequence list", () => {
  it("numbers a name that is taken, regardless of case", () => {
    expect(uniqueName("Install Windows", [])).toBe("Install Windows");
    expect(uniqueName("Install Windows", ["install windows ", "Install Windows 2"])).toBe(
      "Install Windows 3",
    );
  });

  it("gives every step of a template an id of its own", () => {
    const definition = { version: 1, steps: [newStep("reboot", "a"), newStep("reboot", "b")] };
    const copy = withNewStepIds(definition);

    expect(copy.steps.map((step) => step.id)).not.toContain("a");
    expect(new Set(copy.steps.map((step) => step.id)).size).toBe(2);
  });

  it("names a rule by its place and its name", () => {
    expect(ruleTarget(rule({}))).toBe("Rule 1, Latitude laptops");
    expect(ruleTarget(rule({ position: 3, name: "Kiosk" }))).toBe("Rule 4, Kiosk");
  });

  it("counts the machines a sequence is assigned to or running on", () => {
    const id = "0193a4b2-0000-7000-8000-0000000000e1";
    const machines = [
      machineSummary({ deployment: deploymentSummary({ state: "Running", sequenceId: id }) }),
      machineSummary({ deployment: deploymentSummary({ state: "Assigned", sequenceId: id }) }),
      machineSummary({ deployment: deploymentSummary({ state: "Done", sequenceId: id }) }),
      machineSummary({ deployment: deploymentSummary({ state: "Running", sequenceId: "x" }) }),
    ];

    expect(activeRunsOf(machines, id)).toBe(2);
  });

  it("says which rules keep a sequence from being deleted", () => {
    expect(deletionBlocker(summary(), [])).toBeNull();
    expect(deletionBlocker(summary(), [rule({})])).toBe(
      "Rule 1, Latitude laptops chooses Install Windows. Let that rule choose another sequence or none, then delete this one.",
    );
    expect(
      deletionBlocker(summary(), [rule({}), rule({ id: "r2", position: 3, name: "Kiosk" })]),
    ).toBe(
      "Rule 1, Latitude laptops and rule 4, Kiosk choose Install Windows. Let those rules choose another sequence or none, then delete this one.",
    );
  });

  it("says what a deletion does to the machines using the sequence", () => {
    expect(deletionConsequence(summary(), 0)).toEqual([
      "Install Windows is deleted and can no longer be assigned or chosen at a machine.",
      "Runs that already ended keep their history.",
    ]);
    expect(deletionConsequence(summary(), 3)[1]).toBe(
      "The 3 machines it is assigned to or running on keep the copy they got and finish with it.",
    );
  });

  it("filters by whether a sequence can run, and finds it by every word typed", () => {
    const draft = summary({ problemCount: 1, description: "For the lab in room 4" });

    expect(inFilter(draft, "problems")).toBe(true);
    expect(inFilter(draft, "ready")).toBe(false);
    expect(inFilter(summary(), "ready")).toBe(true);
    expect(matchesSearch(draft, "  windows  LAB ")).toBe(true);
    expect(matchesSearch(draft, "windows kiosk")).toBe(false);
    expect(matchesSearch(draft, "")).toBe(true);
  });

  it("says what running a sequence does, and counts its findings", () => {
    expect(
      sequenceFacts(
        summary({ rawImageName: "noble", continuesInWindows: true, needsComputerName: true }),
      ),
    ).toEqual([
      "Erases the disk",
      "writes noble",
      "goes on in the installed Windows",
      "needs a computer name",
    ]);
    expect(sequenceFacts(summary({ erasesDisk: false }))).toEqual(["Keeps the disk"]);
    expect(findingCounts(0, 0)).toBeNull();
    expect(findingCounts(2, 1)).toBe("2 problems, 1 warning");
    expect(findingCounts(0, 3)).toBe("3 warnings");
  });

  it("works out the list's entry from a saved copy as the server does", () => {
    const seed = {
      ...(newStep("writeCloudInitSeed", "c") as WriteCloudInitSeedStep),
      metaData: "instance-id: x\n",
      userData: '#cloud-config\nhostname: "{{ ComputerName }}"\n',
    };

    expect(summaryOf(view())).toMatchObject({
      revision: 4,
      stepCount: 2,
      problemCount: 1,
      erasesDisk: true,
      needsComputerName: true,
      continuesInWindows: true,
      updatedBy: "bob",
    });
    expect(
      summaryOf(
        view({
          definition: { version: 2, steps: [newStep("writeRawImage", "w"), seed] },
          stepPhases: ["WindowsPE", "WindowsPE"],
        }),
        summary({ rawImageName: "noble", rawImageBootCapability: "NotSigned" }),
      ),
    ).toMatchObject({
      erasesDisk: true,
      needsComputerName: true,
      continuesInWindows: false,
      rawImageName: "noble",
      rawImageBootCapability: "NotSigned",
    });
  });

  it("puts a saved copy into the list in the server's order, and never an older one over a newer", () => {
    const queryClient = new QueryClient();
    const other = summary({ id: "0193a4b2-0000-7000-8000-0000000000e0", name: "Kiosk" });

    queryClient.setQueryData(["sequences"], [summary(), other]);
    upsertSummary(queryClient, view({ name: "Zebra lab" }));

    expect(
      queryClient.getQueryData<SequenceSummary[]>(["sequences"])?.map((entry) => entry.name),
    ).toEqual(["Kiosk", "Zebra lab"]);

    upsertSummary(queryClient, view({ name: "Stale", revision: 2 }));
    expect(
      queryClient.getQueryData<SequenceSummary[]>(["sequences"])?.map((entry) => entry.name),
    ).toEqual(["Kiosk", "Zebra lab"]);

    removeSummary(queryClient, other.id);
    expect(
      queryClient.getQueryData<SequenceSummary[]>(["sequences"])?.map((entry) => entry.name),
    ).toEqual(["Zebra lab"]);
  });

  it("leaves a list that was never read alone", () => {
    const queryClient = new QueryClient();

    upsertSummary(queryClient, view());

    expect(queryClient.getQueryData(["sequences"])).toBeUndefined();
  });

  it("keeps only search parameters it knows", () => {
    expect(sequencesSearch({ state: "problems", q: "lab", other: 1 })).toEqual({
      state: "problems",
      q: "lab",
    });
    expect(sequencesSearch({ state: "all", q: "" })).toEqual({});
    expect(sequencesSearch({ state: "broken" })).toEqual({});
    expect(sequenceSearch({ step: "s" })).toEqual({ step: "s" });
    expect(sequenceSearch({ step: 4 })).toEqual({});
  });
});
