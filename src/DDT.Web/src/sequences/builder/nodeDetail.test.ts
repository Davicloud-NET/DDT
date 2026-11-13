// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { afterEach, describe, expect, it } from "vitest";

import { leaf } from "@/test/trees";

import type {
  InstallApplicationsStep,
  ScriptInterpreter,
  SequencePhase,
  SequenceStep,
  WriteUnattendStep,
} from "../sequences";
import { newStep } from "../steps";
import { nodeDetail } from "./nodeDetail";

function line(node: SequenceStep): string {
  return nodeDetail(node, {
    catalog: { images: [], packages: [], applications: [], bundles: [], domainConfigured: null },
    subjects: [],
    findings: { problems: [], warnings: [] },
    accounts: null,
  }).text;
}

function scriptLine(interpreter: ScriptInterpreter, phase: SequencePhase): string {
  return line({ ...leaf("a"), interpreter, phase });
}

describe("the line under a script's name on its card", () => {
  afterEach(() => {
    i18n.loadAndActivate({ locale: "en", messages: {} });
  });

  it("says where the script runs as a sentence does", () => {
    expect(scriptLine("Cmd", "WindowsPE")).toBe("cmd, in Windows PE");
    expect(scriptLine("PowerShell", "Windows")).toBe("PowerShell, in Windows");
    expect(scriptLine("Shell", "LinuxBoot")).toBe("Shell, in the Linux boot environment");
    expect(scriptLine("Shell", "Linux")).toBe("Shell, in the installed Linux");
  });

  it("says it in German with the preposition the phase takes", async () => {
    const { messages } = await import("../../locales/de/messages.po");
    i18n.loadAndActivate({ locale: "de", messages });

    expect(scriptLine("Shell", "Linux")).toBe("Shell, im installierten Linux");
    expect(scriptLine("Shell", "LinuxBoot")).toBe("Shell, in der Linux-Startumgebung");
    expect(scriptLine("PowerShell", "Windows")).toBe("PowerShell, in Windows");
  });
});

describe("the line under an Install applications step's name", () => {
  const install = newStep("installApplications", "i") as InstallApplicationsStep;
  const office = { id: "o", kind: "Bundle" as const };
  const reader = { id: "r", kind: "Application" as const };

  it("counts what it installs, and names the value that adds more", () => {
    expect(line(install)).toBe("No applications chosen yet");
    expect(line({ ...install, applications: [office] })).toBe("1 application or bundle");
    expect(line({ ...install, applications: [office, reader], variable: "Extras" })).toBe(
      "2 applications and bundles, then those Extras names",
    );
    expect(line({ ...install, variable: "Extras" })).toBe("The applications Extras names");
  });
});

describe("the line under a Write the answer file step's name", () => {
  const answers = newStep("writeUnattend", "u") as WriteUnattendStep;

  it("names its own language and organization, and pages it shows", () => {
    expect(line(answers)).toBe("For Windows setup at its first start");
    expect(line({ ...answers, uiLanguage: "de-DE", organization: "Davicloud" })).toBe(
      "de-DE, Davicloud",
    );
    expect(line({ ...answers, hideWirelessSetup: false })).toBe("shows some setup pages");
  });
});
