// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { leaf } from "@/test/trees";

import type { SequencePhase } from "../../sequences";
import { NodeSummary } from "./NodeSummary";

function renderSummary(...phases: SequencePhase[]) {
  render(
    <I18nProvider i18n={i18n}>
      <NodeSummary
        node={leaf("a")}
        place="Step 1"
        phases={phases}
        findings={{ problems: [], warnings: [] }}
      />
    </I18nProvider>,
  );
}

describe("where the inspector says a node runs", () => {
  afterEach(() => {
    i18n.loadAndActivate({ locale: "en", messages: {} });
  });

  it("names a phase as a sentence does", () => {
    renderSummary("Linux");

    expect(screen.getByText("Run script, in the installed Linux.")).toBeInTheDocument();
  });

  it("names both phases of a step that may run in either", () => {
    renderSummary("LinuxBoot", "Linux");

    expect(
      screen.getByText("Run script, in the Linux boot environment or in the installed Linux."),
    ).toBeInTheDocument();
  });

  it("keeps the Windows phases as they read", () => {
    renderSummary("WindowsPE", "Windows");

    expect(screen.getByText("Run script, in Windows PE or in Windows.")).toBeInTheDocument();
  });

  it("gives each phase its own preposition in German", async () => {
    const { messages } = await import("../../../locales/de/messages.po");
    i18n.loadAndActivate({ locale: "de", messages });
    renderSummary("LinuxBoot", "Linux");

    expect(
      screen.getByText(/, in der Linux-Startumgebung oder im installierten Linux\.$/),
    ).toBeInTheDocument();
  });
});
