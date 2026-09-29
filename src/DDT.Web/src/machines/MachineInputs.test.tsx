// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { MachineSummary } from "@/machines/machines";
import type { InputDeclaration } from "@/sequences/sequences";
import { fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  deploymentOptions,
  deploymentSummary,
  machineSummary,
  operator,
  sequenceResolution,
  sequenceSummary,
  sequenceView,
} from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import type { Routes } from "@/test/server";

// The inputs a sequence asks for on the web before it runs. They're asked when a run is assigned, and when a waiting
// machine is approved with the sequence a rule chose.

const sequenceId = "0193a4b2-0000-7000-8000-0000000000e1";
const installWindows = sequenceSummary({ id: sequenceId });
const label = "Virtual Machine (00:15:5D:01:02:03)";
const password = "Correct-Horse-7";

function input(overrides: Partial<InputDeclaration> & Pick<InputDeclaration, "name">) {
  return {
    label: overrides.name,
    help: null,
    kind: "Text" as const,
    choices: [],
    default: null,
    required: false,
    maxLength: null,
    askAt: "Both" as const,
    account: null,
    ...overrides,
  };
}

const inputs: InputDeclaration[] = [
  input({
    name: "Department",
    kind: "Choice",
    required: true,
    choices: [
      { value: "Sales", label: null },
      { value: "Finance", label: null },
      { value: "Service", label: "Customer service" },
    ],
  }),
  input({ name: "OfficeCode", label: "Office code", required: true, askAt: "Web" }),
  input({
    name: "Printers",
    kind: "MultiChoice",
    choices: [
      { value: "P1", label: "First floor" },
      { value: "P2", label: "Second floor" },
    ],
  }),
  input({ name: "Kiosk", kind: "YesNo" }),
  input({ name: "AssetTag", label: "Asset tag", askAt: "Machine", required: true }),
  input({
    name: "JoinAccount",
    label: "Join account",
    kind: "Account",
    required: true,
    account: { domain: "corp.example", hosts: [], runAs: false },
  }),
];

function chosenByRule() {
  return sequenceResolution({
    source: "Rule",
    sequenceId,
    sequenceName: "Install Windows",
    ruleId: "0193a4b2-0000-7000-8000-0000000000f2",
    explanation: "The rule Berlin office chooses Install Windows.",
    inputs,
    inputDefaults: [
      {
        name: "Department",
        value: "Sales",
        source: "Rule",
        sourceId: "0193a4b2-0000-7000-8000-0000000000f2",
        sourceName: "Berlin office",
        overridden: false,
      },
    ],
  });
}

function routes(machine: MachineSummary, extra: Routes = {}): Routes {
  return {
    "GET /api/machines": { body: [machine] },
    "GET /api/sequences": { body: [installWindows] },
    [`GET /api/sequences/${sequenceId}`]: { body: sequenceView(installWindows, []) },
    "GET /api/deployments/options": { body: deploymentOptions() },
    [`GET /api/machines/${machine.id}/sequence`]: { body: chosenByRule() },
    ...extra,
  };
}

async function openAssign(): Promise<HTMLElement> {
  press(await screen.findByRole("button", { name: "Assign" }));

  return screen.findByRole("dialog", { name: `Assign a task sequence to ${label}` });
}

describe("inputs asked on the web", () => {
  it("asks a sequence's inputs when it is assigned, starting from the machine's defaults, and sends the answers", async () => {
    const machine = machineSummary();
    const { server } = await renderPage({
      path: "/machines",
      user: operator,
      routes: routes(machine, {
        [`POST /api/machines/${machine.id}/deployments`]: {
          body: { ...machine, deployment: deploymentSummary() },
        },
      }),
    });

    const dialog = await openAssign();
    const department = await within(dialog).findByRole("radiogroup", {
      name: "Department (required)",
    });

    expect(within(department).getByRole("radio", { name: "Sales" })).toBeChecked();
    expect(within(dialog).getByText("Filled in from the rule Berlin office.")).toBeInTheDocument();
    expect(within(dialog).getByText("It joins the domain corp.example.")).toBeInTheDocument();
    // Asked only at the machine, so not here.
    expect(within(dialog).queryByLabelText(/Asset tag/)).not.toBeInTheDocument();

    const assign = within(dialog).getByRole("button", { name: "Assign sequence" });
    await waitFor(() => {
      expect(assign).toBeEnabled();
    });
    press(assign);

    // A required answer is missing, so nothing is sent and its field says so.
    expect(await within(dialog).findAllByText("Enter an answer.")).toHaveLength(1);
    expect(within(dialog).getByText("Enter the user name and the password.")).toBeInTheDocument();
    expect(server.changes()).toEqual([]);

    fill(within(dialog).getByRole("textbox", { name: "Office code (required)" }), "BER-2");
    press(within(dialog).getByRole("checkbox", { name: "Second floor" }));
    press(within(dialog).getByRole("radio", { name: "Yes" }));
    fill(within(dialog).getByRole("textbox", { name: "User name for Join account" }), "CORP\\join");
    fill(within(dialog).getByLabelText("Password for Join account"), password);
    press(assign);

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => request.body)).toEqual([
      {
        sequenceId,
        computerName: null,
        answers: [
          { name: "Department", value: "Sales" },
          { name: "OfficeCode", value: "BER-2" },
          { name: "Printers", value: "P2" },
          { name: "Kiosk", value: "true" },
          { name: "JoinAccount", value: null, userName: "CORP\\join", password },
        ],
      },
    ]);
  });

  it("shows the server's refusal of an answer under its field, and never shows a password", async () => {
    const machine = machineSummary();
    await renderPage({
      path: "/machines",
      user: operator,
      routes: routes(machine, {
        [`POST /api/machines/${machine.id}/deployments`]: {
          status: 400,
          body: {
            title: "One or more validation errors occurred.",
            errors: {
              "answers.OfficeCode": ["Office code: enter at most 8 characters."],
              "answers.JoinAccount": ["The domain refused the user name or the password."],
            },
          },
        },
      }),
    });

    const dialog = await openAssign();
    fill(
      await within(dialog).findByRole("textbox", { name: "Office code (required)" }),
      "BERLIN-OFFICE-2",
    );
    fill(within(dialog).getByRole("textbox", { name: "User name for Join account" }), "CORP\\join");
    const secret = within(dialog).getByLabelText("Password for Join account");
    fill(secret, password);
    press(within(dialog).getByRole("button", { name: "Assign sequence" }));

    expect(
      await within(dialog).findByText("Office code: enter at most 8 characters."),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText("The domain refused the user name or the password."),
    ).toBeInTheDocument();
    // The field errors are the whole refusal, so no notice repeats them.
    expect(within(dialog).queryByText("One or more validation errors occurred.")).toBeNull();
    expect(secret).toHaveAttribute("type", "password");
    expect(document.body.textContent).not.toContain(password);
    await expectNoAxeViolations();
  });

  it("asks the inputs with an approval that runs the rule's sequence, and sends the answers with it", async () => {
    const machine = machineSummary({ state: "Pending", everApproved: false });
    const { server } = await renderPage({
      path: "/machines",
      user: operator,
      routes: routes(machine, {
        [`POST /api/machines/${machine.id}/approve`]: {
          body: {
            ...machine,
            state: "Approved",
            everApproved: true,
            deployment: deploymentSummary({ source: "Rule" }),
          },
        },
      }),
    });

    press(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: `Approve ${label}?` });

    expect(
      within(dialog).getByText(
        `Approving ${label} also runs Install Windows on it, which a rule chose. All data on its disk is erased.`,
      ),
    ).toBeInTheDocument();
    await expectNoAxeViolations();

    fill(within(dialog).getByRole("textbox", { name: "Office code (required)" }), "BER-2");
    fill(within(dialog).getByRole("textbox", { name: "User name for Join account" }), "CORP\\join");
    fill(within(dialog).getByLabelText("Password for Join account"), password);
    press(within(dialog).getByRole("button", { name: "Approve and run Install Windows" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => request.body)).toEqual([
      {
        expectedSequenceId: sequenceId,
        answers: [
          { name: "Department", value: "Sales" },
          { name: "OfficeCode", value: "BER-2" },
          { name: "JoinAccount", value: null, userName: "CORP\\join", password },
        ],
      },
    ]);
  });

  it("asks another sequence's inputs as its document declares them", async () => {
    const machine = machineSummary();
    const other = sequenceSummary({
      id: "0193a4b2-0000-7000-8000-0000000000e3",
      name: "Lab PCs",
    });
    await renderPage({
      path: "/machines",
      user: operator,
      routes: routes(machine, {
        "GET /api/sequences": { body: [installWindows, other] },
        [`GET /api/machines/${machine.id}/sequence`]: { body: sequenceResolution() },
        [`GET /api/sequences/${sequenceId}`]: {
          body: {
            ...sequenceView(installWindows, []),
            definition: {
              version: 3,
              steps: [],
              inputs: [input({ name: "Room", default: "B-204", help: "Where it stands." })],
            },
          },
        },
      }),
    });

    const dialog = await openAssign();
    const room = await within(dialog).findByRole("textbox", { name: "Room" });

    expect(room).toHaveValue("B-204");
    expect(within(dialog).getByText("Where it stands.")).toBeInTheDocument();
  });
});
