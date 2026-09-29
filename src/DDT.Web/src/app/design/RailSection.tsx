// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ProgressBar } from "@/ui/ProgressBar";
import { type RailStep, SequenceRail } from "@/ui/SequenceRail";
import { SequenceRailStrip } from "@/ui/SequenceRailStrip";

import { DesignSection } from "./DesignSection";

const rail: RailStep[] = [
  { state: "done", name: "Partition the disk", meta: "6 s" },
  { state: "running", percent: 62, name: "Apply image", meta: "62%" },
  { state: "waiting", name: "Inject drivers" },
  { state: "skipped", name: "Write the answer file", meta: "skipped" },
  { state: "waiting", name: "Restart into Windows" },
  { state: "waiting", name: "Join the domain" },
  { state: "failed", name: "Run script: baseline", meta: "failed" },
  { state: "waiting", name: "Restart" },
];

export function RailSection() {
  return (
    <DesignSection title="Sequence rail">
      <SequenceRailStrip
        steps={rail}
        label="Step 2 of 8 running, 62 percent"
        className="max-w-md"
      />
      <SequenceRail
        steps={rail}
        phases={[
          { label: "In Windows PE", steps: 5 },
          { label: "In the installed Windows", steps: 3 },
        ]}
        describe={(step, index) =>
          `Step ${String(index + 1)}, ${typeof step.name === "string" ? step.name : ""}, ${step.state}`
        }
      />
      <ProgressBar label="Uploading win11-24h2.wim" value={42} className="max-w-md" />
      <ProgressBar label="Checking the image" className="max-w-md" />
    </DesignSection>
  );
}
