// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { LinuxDetours } from "./LinuxDetours";

function renderDetours(count: number) {
  render(
    <I18nProvider i18n={i18n}>
      <LinuxDetours count={count} />
    </I18nProvider>,
  );
}

// The server counts each such start and ends the run at the fifth in a row.
describe("a machine that starts from the network while its run goes on in the installed Linux", () => {
  it("says that it did once, where it was sent and when the server ends the run", () => {
    renderDetours(1);

    expect(
      screen.getByText(
        "The machine started from the network once and was sent back to its disk. At 5 such starts in a row, the run ends.",
      ),
    ).toBeInTheDocument();
  });

  it("counts the starts in a row", () => {
    renderDetours(4);

    expect(
      screen.getByText(
        "The machine started from the network 4 times in a row and was sent back to its disk. At 5 such starts in a row, the run ends.",
      ),
    ).toBeInTheDocument();
  });

  it("says it in German", async () => {
    const { messages } = await import("../../locales/de/messages.po");

    i18n.loadAndActivate({ locale: "de", messages });

    try {
      renderDetours(1);
      renderDetours(3);

      expect(
        screen.getByText(
          "Das Gerät wurde einmal per Netzwerk gestartet und wieder von seinem Datenträger gestartet. Bei 5 solchen Starts in Folge endet die Ausführung.",
        ),
      ).toBeInTheDocument();
      expect(
        screen.getByText(
          "Das Gerät wurde 3-mal in Folge per Netzwerk gestartet und wieder von seinem Datenträger gestartet. Bei 5 solchen Starts in Folge endet die Ausführung.",
        ),
      ).toBeInTheDocument();
    } finally {
      i18n.loadAndActivate({ locale: "en", messages: {} });
    }
  });
});
