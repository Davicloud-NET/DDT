// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, group, leaf } from "@/test/trees";

import { nodeLabel, slotLabel } from "./flowLabels";
import { indexTree } from "./flowTree";

describe("what a screen reader hears", () => {
  const named = indexTree([
    leaf("a"),
    {
      ...branch("b", [leaf("c"), { ...leaf("d"), kind: "reboot", name: "Restart" } as never], []),
      name: "Is it a Latitude?",
    },
    group("f"),
  ]);

  it("says where a node is, its name, its kind and its findings", () => {
    expect(nodeLabel(named, "a", 0, 0)).toBe("Step 1, Step a, Run script");
    expect(nodeLabel(named, "c", 1, 0)).toBe(
      "Step 1 of Then of 'If: Is it a Latitude?', Step c, Run script, 1 problem",
    );
    expect(nodeLabel(named, "b", 0, 2)).toBe("Step 2, If: Is it a Latitude?, 2 warnings");
  });

  it("says where a gap is", () => {
    expect(slotLabel(named, { parent: null, body: "steps", index: 0 })).toBe(
      "Add a step before Step a",
    );
    expect(slotLabel(named, { parent: null, body: "steps", index: 1 })).toBe(
      "Add a step between Step a and If: Is it a Latitude?",
    );
    expect(slotLabel(named, { parent: null, body: "steps", index: 3 })).toBe(
      "Add a step after Group: Group f",
    );
    expect(slotLabel(named, { parent: "b", body: "else", index: 0 })).toBe(
      "Add a step to Else of 'If: Is it a Latitude?'",
    );
    expect(slotLabel(named, { parent: "f", body: "steps", index: 0 })).toBe(
      "Add a step to 'Group: Group f'",
    );
    expect(slotLabel(indexTree([]), { parent: null, body: "steps", index: 0 })).toBe(
      "Add the first step",
    );
  });
});
