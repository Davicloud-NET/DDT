// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { HardwareModelCount } from "@/machines/machines";
import type { AssignmentRuleView, SaveAssignmentRuleRequest } from "@/rules/rules";
import { chooseMenuItem, chooseOption, fill, press, selectKey, selectOptions } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  administrator,
  assignmentRule,
  machineSummary,
  operator,
  sequenceSummary,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const now = new Date("2026-09-16T10:02:00Z");

const sequences = [
  sequenceSummary({ id: "s1", name: "Install Windows" }),
  sequenceSummary({ id: "s2", name: "Lab PCs" }),
  sequenceSummary({ id: "s3", name: "Draft", problemCount: 2 }),
];

const modelRule = assignmentRule({ id: "r1", sequenceId: "s1" });

const macRule = assignmentRule({
  id: "r2",
  kind: "Mac",
  mac: "00155D010203",
  manufacturer: null,
  model: null,
  sequenceId: "s2",
  sequenceName: "Lab PCs",
});

const models: HardwareModelCount[] = [
  { manufacturer: "Dell Inc.", model: "Latitude 7440", machines: 3 },
  { manufacturer: "Dell Inc.", model: "Latitude 5440", machines: 2 },
];

function open(
  rules: AssignmentRuleView[],
  routes: Routes = {},
  user = administrator,
): Promise<RenderedPage> {
  return renderPage({
    path: "/deployment/rules",
    user,
    routes: {
      "GET /api/rules": { body: rules },
      "GET /api/sequences": { body: sequences },
      "GET /api/machines": { body: [machineSummary()] },
      "GET /api/machines/models": { body: models },
      ...routes,
    },
  });
}

function rulesOf(kind: "hardware model" | "MAC address"): HTMLElement {
  return screen.getByRole("grid", { name: `Rules by ${kind}` });
}

function row(table: HTMLElement, target: string): HTMLElement {
  return within(table).getByRole("row", {
    name: new RegExp(`^${target.replace(/[\\^$.*+?()[\]{}|]/g, "\\$&")}`),
  });
}

function actionsFor(description: string): HTMLElement {
  return screen.getByRole("button", { name: `Actions for the rule for ${description}` });
}

describe("RulesPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("says that rules only choose and never authorize, and what to do without any", async () => {
    await open([]);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Assignment rules" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /^A rule chooses the task sequence for a machine that has none\. It never authorizes a machine: someone still signs in at it, or an operator approves it\./,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/comes first; then a rule for one of the machine's MAC addresses/),
    ).toBeInTheDocument();
    expect(await screen.findByText("No rules yet")).toBeInTheDocument();
    expect(
      screen.getByText(/Add a rule to choose one by hardware model or MAC address\./),
    ).toBeInTheDocument();
  });

  it("lists the rules by kind with their sequence and the machines they match", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });
    await open([modelRule, macRule]);

    await screen.findByRole("grid", { name: "Rules by hardware model" });
    const model = within(row(rulesOf("hardware model"), "Dell Inc. Latitude*"));
    const mac = within(row(rulesOf("MAC address"), "00:15:5D:01:02:03"));

    expect(model.getByRole("link", { name: "Install Windows" })).toHaveAttribute(
      "href",
      "/deployment/sequences/s1",
    );
    expect(model.getByText("5 machines")).toBeInTheDocument();
    expect(model.getByText("2 minutes ago by admin")).toBeInTheDocument();

    expect(mac.getByRole("link", { name: "Lab PCs" })).toBeInTheDocument();
    expect(mac.getByText("1 machine")).toBeInTheDocument();
  });

  it("offers only sequences that can run", async () => {
    await open([modelRule]);

    await screen.findByRole("grid", { name: "Rules by hardware model" });
    await chooseMenuItem(actionsFor("model Dell Inc. Latitude*"), "Change");
    const dialog = await screen.findByRole("dialog", { name: "Change the rule" });

    expect(await selectOptions(dialog, "Task sequence")).toEqual([
      { text: "Install Windows", disabled: false },
      { text: "Lab PCs", disabled: false },
      { text: "Draft2 problems, cannot run", disabled: true },
    ]);
  });

  it("changes a rule in its dialog and shows the server's answer without reading the rules again", async () => {
    const { server } = await open([modelRule], {
      "PUT /api/rules/r1": (request) =>
        json({
          ...modelRule,
          sequenceId: (request.body as SaveAssignmentRuleRequest).sequenceId,
          sequenceName: "Lab PCs",
        }),
    });

    await screen.findByRole("grid", { name: "Rules by hardware model" });
    await chooseMenuItem(actionsFor("model Dell Inc. Latitude*"), "Change");
    const dialog = await screen.findByRole("dialog", { name: "Change the rule" });

    expect(within(dialog).getByRole("combobox", { name: "Model" })).toHaveValue("Latitude*");
    expect(selectKey(dialog, "Task sequence")).toHaveTextContent("Install Windows");
    // A rule keeps its kind.
    expect(within(dialog).queryByRole("radiogroup")).not.toBeInTheDocument();

    await chooseOption(dialog, "Task sequence", "Lab PCs");
    expect(server.changes()).toEqual([]);
    press(within(dialog).getByRole("button", { name: "Save rule" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(server.changes().map((request) => [request.method, request.path, request.body])).toEqual(
      [
        [
          "PUT",
          "/api/rules/r1",
          {
            kind: "Model",
            mac: null,
            manufacturer: "Dell Inc.",
            model: "Latitude*",
            sequenceId: "s2",
            description: null,
          },
        ],
      ],
    );
    expect(
      within(row(rulesOf("hardware model"), "Dell Inc. Latitude*")).getByRole("link", {
        name: "Lab PCs",
      }),
    ).toBeInTheDocument();
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("shows another administrator's change from the hub, and keeps an open dialog's own edit", async () => {
    const { server, hub } = await open([modelRule, macRule]);

    await screen.findByRole("grid", { name: "Rules by MAC address" });
    await chooseMenuItem(actionsFor("MAC 00:15:5D:01:02:03"), "Change");
    const dialog = await screen.findByRole("dialog", { name: "Change the rule" });
    fill(within(dialog).getByRole("textbox", { name: "Description" }), "Room 4");

    act(() => {
      hub?.push("rulesChanged", [
        { ...modelRule, sequenceId: "s2", sequenceName: "Lab PCs", updatedBy: "bob" },
        { ...macRule, description: "Lab bench", updatedBy: "bob" },
      ]);
    });

    expect(await screen.findByText("Lab bench")).toBeInTheDocument();
    expect(within(dialog).getByRole("textbox", { name: "Description" })).toHaveValue("Room 4");
    expect(server.count("GET /api/rules")).toBe(1);

    press(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(
      within(row(rulesOf("hardware model"), "Dell Inc. Latitude*")).getByRole("link", {
        name: "Lab PCs",
      }),
    ).toBeInTheDocument();
  });

  it("adds a rule only with Add, and shows a duplicate at its MAC address", async () => {
    const created: SaveAssignmentRuleRequest[] = [];
    const { server } = await open([macRule], {
      "POST /api/rules": (request) => {
        const body = request.body as SaveAssignmentRuleRequest;
        created.push(body);

        return created.length === 1
          ? json(
              {
                title:
                  "There is a rule for MAC 00:15:5D:01:02:03 already. It chooses Lab PCs; change that rule instead.",
              },
              409,
            )
          : json(
              assignmentRule({
                id: "r3",
                kind: "Mac",
                mac: "00155D0A0B0C",
                model: null,
                manufacturer: null,
              }),
              201,
            );
      },
    });

    press(await screen.findByRole("button", { name: "Add rule" }));
    const dialog = await screen.findByRole("dialog", { name: "Add an assignment rule" });

    press(
      within(
        within(dialog).getByRole("radiogroup", { name: "Which machines the rule is for" }),
      ).getByRole("radio", { name: "One MAC address" }),
    );
    const mac = await within(dialog).findByRole("combobox", { name: "MAC address" });
    fill(mac, "00-15-5D-01-02-03");
    expect(selectKey(dialog, "Task sequence")).toHaveTextContent("Install Windows");
    expect(created).toEqual([]);

    press(within(dialog).getByRole("button", { name: "Add rule" }));

    await waitFor(() => {
      expect(mac).toHaveAttribute("aria-invalid", "true");
    });
    expect(mac).toHaveAccessibleDescription(
      /There is a rule for MAC 00:15:5D:01:02:03 already\. It chooses Lab PCs; change that rule instead\./,
    );
    expect(created).toEqual([
      {
        kind: "Mac",
        mac: "00-15-5D-01-02-03",
        manufacturer: null,
        model: null,
        sequenceId: "s1",
        description: null,
      },
    ]);

    fill(mac, "00:15:5D:0A:0B:0C");
    press(within(dialog).getByRole("button", { name: "Add rule" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(row(rulesOf("MAC address"), "00:15:5D:0A:0B:0C")).toBeInTheDocument();
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("shows the server's refusal of a field at that field", async () => {
    await open([], {
      "POST /api/rules": {
        status: 400,
        body: {
          title: "One or more validation errors occurred.",
          errors: { model: ["Put at least 3 characters before *."] },
        },
      },
    });

    press(await screen.findByRole("button", { name: "Add rule" }));
    const dialog = await screen.findByRole("dialog", { name: "Add an assignment rule" });
    const model = within(dialog).getByRole("combobox", { name: "Model" });
    fill(model, "L*");
    press(within(dialog).getByRole("button", { name: "Add rule" }));

    await waitFor(() => {
      expect(model).toHaveAttribute("aria-invalid", "true");
    });
    expect(model).toHaveAccessibleDescription(/Put at least 3 characters before \*\./);
    expect(within(dialog).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("says how many registered machines a model rule would match while it is typed", async () => {
    await open([]);

    press(await screen.findByRole("button", { name: "Add rule" }));
    const dialog = await screen.findByRole("dialog", { name: "Add an assignment rule" });
    fill(within(dialog).getByRole("combobox", { name: "Model" }), "Latitude 7440");

    expect(within(dialog).getByText("Matches 3 registered machines.")).toBeInTheDocument();

    fill(within(dialog).getByRole("combobox", { name: "Model" }), "OptiPlex*");
    expect(within(dialog).getByText("No registered machine matches yet.")).toBeInTheDocument();
  });

  it("says what deleting a rule changes, and drops it without reading the rules again", async () => {
    const { server } = await open([modelRule], {
      "DELETE /api/rules/r1": { status: 204 },
    });

    await screen.findByRole("grid", { name: "Rules by hardware model" });
    await chooseMenuItem(actionsFor("model Dell Inc. Latitude*"), "Delete");

    const dialog = await screen.findByRole("dialog", {
      name: "Delete the rule for model Dell Inc. Latitude*?",
    });
    expect(dialog).toHaveAccessibleDescription(
      "Machines of model Dell Inc. Latitude* no longer get Install Windows chosen for them; another rule or an operator chooses instead. Machines that already have a run keep it.",
    );

    press(within(dialog).getByRole("button", { name: "Delete rule" }));

    expect(await screen.findByText("No rules yet")).toBeInTheDocument();
    expect(server.changes().map((request) => `${request.method} ${request.path}`)).toEqual([
      "DELETE /api/rules/r1",
    ]);
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("keeps the delete dialog open with the server's reason", async () => {
    await open([macRule], {
      "DELETE /api/rules/r2": { status: 500, body: { title: "The server failed." } },
    });

    await screen.findByRole("grid", { name: "Rules by MAC address" });
    await chooseMenuItem(actionsFor("MAC 00:15:5D:01:02:03"), "Delete");
    const dialog = await screen.findByRole("dialog", {
      name: "Delete the rule for MAC 00:15:5D:01:02:03?",
    });
    expect(dialog).toHaveAccessibleDescription(
      "The machine with MAC 00:15:5D:01:02:03 no longer gets Lab PCs chosen for it; another rule or an operator chooses instead. A machine that already has a run keeps it.",
    );

    press(within(dialog).getByRole("button", { name: "Delete rule" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("The server failed.");
  });

  it("asks for a task sequence before a rule can be added", async () => {
    await open([], { "GET /api/sequences": { body: [] } });

    expect(
      await screen.findByText(
        "A rule needs a task sequence to choose. Create one under Deployment, Task sequences first.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add rule" })).toBeDisabled();
  });

  it("shows an operator the rules without changes", async () => {
    await open([macRule], {}, operator);

    await screen.findByRole("grid", { name: "Rules by MAC address" });
    expect(screen.queryByRole("button", { name: "Add rule" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();
  });

  it("tells an operator without rules that an administrator adds them", async () => {
    await open([], {}, operator);

    expect(
      await screen.findByText(
        "Without rules an operator assigns a sequence to each machine. An administrator adds rules here.",
      ),
    ).toBeInTheDocument();
  });

  describe("accessibility", () => {
    it("has no violations with rules of both kinds listed", async () => {
      await open([modelRule, macRule]);

      await screen.findByRole("grid", { name: "Rules by MAC address" });
      await expectNoAxeViolations();
    });

    it("has no violations without rules", async () => {
      await open([]);

      await screen.findByText("No rules yet");
      await expectNoAxeViolations();
    });

    it("has no violations with the add dialog open", async () => {
      await open([]);

      press(await screen.findByRole("button", { name: "Add rule" }));
      await screen.findByRole("dialog", { name: "Add an assignment rule" });
      await expectNoAxeViolations();
    });

    it("has no violations with a rule's menu and then its delete dialog open", async () => {
      await open([modelRule]);

      await screen.findByRole("grid", { name: "Rules by hardware model" });
      press(actionsFor("model Dell Inc. Latitude*"));
      const menu = await screen.findByRole("menu");
      await expectNoAxeViolations();

      fireEvent.keyDown(menu, { key: "Escape" });
      await waitFor(() => {
        expect(screen.queryByRole("menu")).not.toBeInTheDocument();
      });
      await chooseMenuItem(actionsFor("model Dell Inc. Latitude*"), "Delete");
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });
  });
});
