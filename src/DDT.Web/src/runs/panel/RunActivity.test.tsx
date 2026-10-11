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

function renderActivity(run: Partial<DeploymentSummary>, handedOverUtc: string | null = null) {
  render(
    <I18nProvider i18n={i18n}>
      <RunActivity
        run={deploymentSummary({ state: "Running", ...run })}
        view={null}
        path={null}
        lastSeenUtc={lastSeenUtc}
        handedOverUtc={handedOverUtc}
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
      screen.getByText(
        "No contact for 3 min 0 s. The installed Linux starts before the agent goes on there, which can take a while.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Windows setup/)).not.toBeInTheDocument();
  });

  it("says the same while the Linux boot environment still hands over", () => {
    renderActivity({ activity: "HandingOver", phase: "LinuxBoot" });

    expect(
      screen.getByText(/^No contact for 3 min 0 s\. The installed Linux starts/),
    ).toBeInTheDocument();
  });

  it("names Windows setup for a run that installs Windows", () => {
    renderActivity({ activity: "WaitingForWindowsSetup", phase: "Windows" });

    expect(
      screen.getByText(
        "No contact for 3 min 0 s. Windows setup runs before the agent starts again, which can take a while.",
      ),
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
      /^That is longer than a first start usually takes\. It may still be installing packages\. Otherwise the machine did not start from its disk, the image has no cloud-init or no systemd, the user-data keeps cloud-init from running DDT's script, or that script failed\. Look at the machine's own screen, and in the installed Linux at/;

    it("says since when the run waits for the first call, and what a first start does", () => {
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
      expect(
        screen.getByText(
          "It starts first, and cloud-init sets it up before DDT's agent starts there. Packages it installs take their time.",
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
      expect(screen.queryByText(/^It starts first/)).not.toBeInTheDocument();
    });

    it("lists no causes a second before that", () => {
      renderActivity(handedOver, new Date(now - 10 * 60_000 + 1_000).toISOString());

      expect(screen.getByText(/^It starts first/)).toBeInTheDocument();
      expect(screen.queryByText(causes)).not.toBeInTheDocument();
    });
  });

  // cloud-init runs while the agent is in contact, so no silence is explained.
  it("explains no silence while the agent waits for cloud-init", () => {
    renderActivity({ activity: "WaitingForCloudInit", phase: "Linux" });

    expect(screen.queryByText(/^No contact/)).not.toBeInTheDocument();
  });
});
