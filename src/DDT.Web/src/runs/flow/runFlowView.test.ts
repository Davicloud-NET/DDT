// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { SequencePhase } from "@/sequences/sequences";
import { stepView } from "@/test/builders";
import { group, leaf } from "@/test/trees";

import { runPath } from "../runPath";
import { handover } from "./runFlowView";

// A run of three top-level steps, each reported in its phase.
function pathIn(...phases: SequencePhase[]) {
  const ids = ["a", "b", "c"].slice(0, phases.length);

  return runPath(
    { version: 5, steps: ids.map(leaf) },
    ids.map((stepId, index) =>
      stepView({ stepId, index, state: "Done", phase: phases[index] ?? "WindowsPE" }),
    ),
  );
}

describe("the hand-over line of a run's flow", () => {
  it("is above the first top-level step in the installed Windows", () => {
    expect(handover(pathIn("WindowsPE", "WindowsPE", "Windows"))).toBe("c");
    expect(handover(pathIn("WindowsPE", "Windows", "Windows"))).toBe("b");
  });

  it("is above the first top-level step in the installed Linux", () => {
    expect(handover(pathIn("LinuxBoot", "LinuxBoot", "Linux"))).toBe("c");
    expect(handover(pathIn("LinuxBoot", "Linux", "Linux"))).toBe("b");
  });

  it("is not drawn for a run that stays in its boot environment", () => {
    expect(handover(pathIn("WindowsPE", "WindowsPE", "WindowsPE"))).toBeNull();
    expect(handover(pathIn("LinuxBoot", "LinuxBoot", "LinuxBoot"))).toBeNull();
  });

  // Nothing of the boot environment is above it, so the line would not run across the flow.
  it("is not drawn above the first step", () => {
    expect(handover(pathIn("Linux", "Linux"))).toBeNull();
    expect(handover(pathIn("Windows", "Windows"))).toBeNull();
  });

  it("looks at the top-level nodes only, and at none the server has not reported", () => {
    const steps = [leaf("a"), group("g", leaf("x")), leaf("b")];
    const inside = runPath({ version: 5, steps }, [
      stepView({ stepId: "a", index: 0, phase: "LinuxBoot" }),
      stepView({ stepId: "g", index: 1, kind: "group", phase: "LinuxBoot" }),
      stepView({ stepId: "x", index: 2, phase: "Linux" }),
      stepView({ stepId: "b", index: 3, phase: "Linux" }),
    ]);

    expect(handover(inside)).toBe("b");
    expect(
      handover(runPath({ version: 5, steps }, [stepView({ stepId: "a", phase: "LinuxBoot" })])),
    ).toBeNull();
  });
});
