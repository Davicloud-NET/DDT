// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { afterEach, describe, expect, it } from "vitest";

import { leaf } from "@/test/trees";

import type { ScriptInterpreter, SequencePhase } from "../sequences";
import { nodeDetail } from "./nodeDetail";

function scriptLine(interpreter: ScriptInterpreter, phase: SequencePhase): string {
  return nodeDetail(
    { ...leaf("a"), interpreter, phase },
    {
      catalog: { images: [], packages: [], domainConfigured: null },
      subjects: [],
      findings: { problems: [], warnings: [] },
      accounts: null,
    },
  ).text;
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
