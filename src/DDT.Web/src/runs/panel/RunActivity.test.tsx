// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { DeploymentSummary } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { deploymentSummary } from "@/test/builders";

import { RunActivity } from "./RunActivity";

const lastSeenUtc = "2026-09-16T10:00:00Z";
const now = Date.parse(lastSeenUtc) + 3 * 60_000;

function renderActivity(
  run: Partial<DeploymentSummary>,
  handedOverUtc: string | null = null,
  bootReturns = 0,
  clockOffsetMs = 0,
) {
  render(
    <I18nProvider i18n={i18n}>
      <RunActivity
        run={deploymentSummary({ state: "Running", ...run })}
        view={null}
        path={null}
        lastSeenUtc={lastSeenUtc}
        handedOverUtc={handedOverUtc}
        bootReturns={bootReturns}
        clockOffsetMs={clockOffsetMs}
        now={now}
      />
    </I18nProvider>,
  );
}

function minutesAgo(minutes: number): string {
  return new Date(now - minutes * 60_000).toISOString();
}

describe("what a run does between its steps", () => {
  it("says why a machine handed over to the installed Linux is silent", () => {
    renderActivity({ activity: "HandingOver", phase: "Linux" });

    expect(
      screen.getByText("No contact for 3 min 0 s, as the installed Linux starts."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Windows setup/)).not.toBeInTheDocument();
  });

  it("says the same while the Linux boot environment still hands over", () => {
    renderActivity({ activity: "HandingOver", phase: "LinuxBoot" });

    expect(
      screen.getByText(/^No contact for 3 min 0 s, as the installed Linux starts/),
    ).toBeInTheDocument();
  });

  it("names Windows setup for a run that installs Windows", () => {
    renderActivity({ activity: "WaitingForWindowsSetup", phase: "Windows" });

    expect(
      screen.getByText("No contact for 3 min 0 s, as Windows setup runs."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/installed Linux/)).not.toBeInTheDocument();
  });

  it("says that the machine restarts, whatever the phase", () => {
    renderActivity({ activity: "Restarting", phase: "Linux" });

    expect(
      screen.getByText("No contact for 3 min 0 s, as the machine restarts."),
    ).toBeInTheDocument();
  });

  // Nothing may ever come from the installed Linux, and the server cannot tell a slow first start from none.
  describe("after the hand-over to the installed Linux", () => {
    const handedOver = {
      activity: "Restarting",
      phase: "Linux",
      stepIndex: 1,
      stepCount: 3,
    } as const;
    const causes =
      /^That is longer than a first start usually takes\. Look at the machine's own screen, and in the installed Linux at/;

    it("says since when the run waits for the first call", () => {
      const since = minutesAgo(2);
      const when = new Date(since).toLocaleString(formattingLocale());

      renderActivity({ ...handedOver, stepName: "Write the cloud-init seed" }, since);

      expect(screen.getByText("Waiting for the installed Linux")).toBeInTheDocument();
      expect(screen.getByText("After step 2 of 3: Write the cloud-init seed")).toBeInTheDocument();
      expect(
        screen.getByText(
          `Handed over ${when}, 2 min 0 s ago. The installed Linux has not called yet.`,
        ),
      ).toBeInTheDocument();
      expect(screen.queryByText(causes)).not.toBeInTheDocument();
      expect(screen.queryByText(/^No contact/)).not.toBeInTheDocument();
      expect(screen.queryByText("Restarting")).not.toBeInTheDocument();
    });

    it("says after ten minutes what may keep it and where to look", () => {
      renderActivity(handedOver, minutesAgo(10));

      const said = screen.getByText(causes);

      expect(said).toHaveTextContent(
        /at \/var\/log\/cloud-init-output\.log and journalctl -u ddt-agent\.$/,
      );
      expect(
        screen.getByText(/, 10 min 0 s ago\. The installed Linux has not called yet\.$/),
      ).toBeInTheDocument();
    });

    it("lists no causes a second before that", () => {
      renderActivity(handedOver, new Date(now - 10 * 60_000 + 1_000).toISOString());

      expect(screen.getByText(/The installed Linux has not called yet\.$/)).toBeInTheDocument();
      expect(screen.queryByText(causes)).not.toBeInTheDocument();
    });

    // The hand-over's time is the server's. A browser whose clock is off would count the ten minutes wrong.
    it("measures the wait on the server's clock", () => {
      const ahead = -10 * 60_000;

      renderActivity(handedOver, minutesAgo(12), 0, ahead);

      expect(
        screen.getByText(/, 2 min 0 s ago\. The installed Linux has not called yet\.$/),
      ).toBeInTheDocument();
      expect(screen.queryByText(causes)).not.toBeInTheDocument();
    });

    it("says after ten minutes on the server's clock what may keep it, though the browser's is behind", () => {
      renderActivity(handedOver, minutesAgo(2), 0, 9 * 60_000);

      expect(
        screen.getByText(/, 11 min 0 s ago\. The installed Linux has not called yet\.$/),
      ).toBeInTheDocument();
      expect(screen.getByText(causes)).toBeInTheDocument();
    });

    it("says in German what may keep it", async () => {
      const { messages } = await import("../../locales/de/messages.po");

      i18n.loadAndActivate({ locale: "de", messages });

      try {
        renderActivity(handedOver, minutesAgo(10));

        expect(
          screen.getByText(
            /^Das ist länger, als ein erster Start üblicherweise dauert\. Sehen Sie am Bildschirm des Geräts nach und im installierten Linux in/,
          ),
        ).toBeInTheDocument();
      } finally {
        i18n.loadAndActivate({ locale: "en", messages: {} });
      }
    });

    // The boot environment saw the machine again, said so with the run's token and sent it back to its disk.
    describe("once the machine has started from the network again", () => {
      const again =
        /^DDT ends the run if the machine keeps coming back\. Look at the machine's own screen, and in the installed Linux at/;

      it("says when, for which time and what DDT did, and where to look, at once", () => {
        const since = minutesAgo(1);
        const when = new Date(since).toLocaleString(formattingLocale());

        renderActivity({ ...handedOver, stepName: "Write the cloud-init seed" }, since, 1);

        expect(screen.getByText("Waiting for the installed Linux")).toBeInTheDocument();
        expect(
          screen.getByText("After step 2 of 3: Write the cloud-init seed"),
        ).toBeInTheDocument();
        expect(
          screen.getByText(
            `The machine started from the network again ${when}, 1 min 0 s ago, for the 1st time. The installed Linux has not called since.`,
          ),
        ).toBeInTheDocument();
        expect(screen.getByText(again)).toHaveTextContent(
          /at \/var\/log\/cloud-init-output\.log and journalctl -u ddt-agent\.$/,
        );
        expect(screen.queryByText(/^Handed over/)).not.toBeInTheDocument();
        expect(screen.queryByText(causes)).not.toBeInTheDocument();
      });

      it("counts on", () => {
        renderActivity(handedOver, minutesAgo(11), 2);

        expect(
          screen.getByText(
            /, 11 min 0 s ago, for the 2nd time\. The installed Linux has not called since\.$/,
          ),
        ).toBeInTheDocument();
        expect(screen.getByText(again)).toBeInTheDocument();
        expect(screen.queryByText(causes)).not.toBeInTheDocument();
      });

      it("says it in German", async () => {
        const { messages } = await import("../../locales/de/messages.po");
        const since = minutesAgo(1);

        i18n.loadAndActivate({ locale: "de", messages });

        try {
          const when = new Date(since).toLocaleString(formattingLocale());

          renderActivity(handedOver, since, 2);

          expect(
            screen.getByText(
              `Das Gerät wurde am ${when}, vor 1 Min. 0 s, erneut per Netzwerk gestartet, zum 2. Mal. Das installierte Linux hat sich seitdem nicht gemeldet.`,
            ),
          ).toBeInTheDocument();
          expect(
            screen.getByText(
              /^DDT beendet die Ausführung, wenn das Gerät immer wieder per Netzwerk gestartet wird\./,
            ),
          ).toBeInTheDocument();
        } finally {
          i18n.loadAndActivate({ locale: "en", messages: {} });
        }
      });
    });
  });

  // cloud-init runs while the agent is in contact, so no silence is explained.
  it("explains no silence while the agent waits for cloud-init", () => {
    renderActivity({ activity: "WaitingForCloudInit", phase: "Linux" });

    expect(screen.queryByText(/^No contact/)).not.toBeInTheDocument();
  });
});
