// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { MachineSequenceResolution, RuleView, SaveRuleRequest } from "@/rules/rules";
import { chooseMenuItem, chooseOption, fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import {
  administrator,
  machineRole,
  machineSummary,
  operator,
  ruleView,
  sequenceSummary,
  viewer,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const sequences = [
  sequenceSummary({ id: "s1", name: "Install Windows" }),
  sequenceSummary({ id: "s2", name: "Kiosk" }),
  sequenceSummary({ id: "s3", name: "Draft", problemCount: 2 }),
];

const roles = [
  machineRole({ id: "role1", name: "Office PC", ruleCount: 1 }),
  machineRole({ id: "role2", name: "Kiosk", ruleCount: 1 }),
];

const kiosk = ruleView({
  id: "r1",
  position: 0,
  name: "Kiosk in the lobby",
  when: { kind: "test", variable: "MacAddress", operator: "Equals", value: "3C:52:82:6A:1F:0B" },
  sequenceId: "s2",
  sequenceName: "Kiosk",
  roleIds: ["role2"],
  matchingMachines: 1,
});

const berlin = ruleView({
  id: "r2",
  position: 1,
  name: "Berlin office",
  when: { kind: "test", variable: "DefaultGateway", operator: "Equals", value: "10.21.0.1" },
  sequenceId: null,
  sequenceName: null,
  values: [{ name: "TimeZone", value: "W. Europe Standard Time" }],
  roleIds: ["role1"],
  matchingMachines: 34,
});

const laptops = ruleView({
  id: "r3",
  position: 2,
  name: "Latitude laptops",
  sequenceId: "s1",
  sequenceName: "Install Windows",
  matchingMachines: 58,
});

const machine = machineSummary({
  id: "m1",
  assignedName: "PC-042",
  manufacturer: "Dell Inc.",
  model: "Latitude 7450",
});

function open(
  rules: RuleView[],
  routes: Routes = {},
  user = administrator,
  path = "/deployment/rules",
): Promise<RenderedPage> {
  return renderPage({
    path,
    user,
    routes: {
      "GET /api/rules": { body: rules },
      "GET /api/machine-roles": { body: roles },
      "GET /api/sequences": { body: sequences },
      "GET /api/sequences/facts": { body: [] },
      "GET /api/machines": { body: [machine] },
      "GET /api/accounts": { body: [] },
      ...routes,
    },
  });
}

// The list, also while a drawer or a dialog hides it from assistive technology.
function list(): HTMLElement {
  return screen.getByRole("grid", { name: "Rules in order", hidden: true });
}

// The rules' ids in the order the list shows them.
function order(): (string | null)[] {
  return within(list())
    .getAllByRole("row", { hidden: true })
    .map((row) => row.getAttribute("data-rule-id"));
}

function row(name: string): HTMLElement {
  return within(list()).getByRole("row", { name: new RegExp(name), hidden: true });
}

function actionsFor(name: string): HTMLElement {
  return screen.getByRole("button", { name: `Actions for ${name}` });
}

// The list as the server numbers it after a move.
function numbered(rules: RuleView[]): RuleView[] {
  return rules.map((rule, position) => ({ ...rule, position }));
}

function deferred<T>() {
  let resolve: (value: T) => void = () => undefined;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });

  return { promise, resolve };
}

describe("RulesPage", () => {
  it("says that rules are checked from the top and never approve, and what to do without any", async () => {
    await open([]);

    expect(await screen.findByRole("heading", { level: 1, name: "Rules" })).toBeInTheDocument();
    expect(
      screen.getByText(
        "Checked from the top. The first rule that chooses a sequence chooses it, and the first rule that sets a value sets it. A rule never approves a machine.",
      ),
    ).toBeInTheDocument();
    expect(await screen.findByText("No rules yet")).toBeInTheDocument();
    expect(
      screen.getByText(/Add a rule to choose one, set values or give machine roles/),
    ).toBeInTheDocument();
  });

  it("lists the rules in order with their condition, what they do and the machines they match", async () => {
    await open([kiosk, berlin, { ...laptops, enabled: false, problems: [problem("when")] }]);

    await screen.findByRole("grid", { name: "Rules in order" });
    expect(order()).toEqual(["r1", "r2", "r3"]);

    const first = within(row("Kiosk in the lobby"));
    expect(first.getByText("01")).toBeInTheDocument();
    expect(
      first.getByText("Matches machines where MAC address equals 3C:52:82:6A:1F:0B."),
    ).toBeInTheDocument();
    expect(first.getByText("Chooses Kiosk")).toBeInTheDocument();
    expect(first.getByText("Gives Kiosk")).toBeInTheDocument();
    expect(first.getByText("1 machine")).toBeInTheDocument();

    const second = within(row("Berlin office"));
    expect(second.getByText("Sets TimeZone")).toBeInTheDocument();
    expect(second.getByText("Gives Office PC")).toBeInTheDocument();
    expect(second.getByText("34 machines")).toBeInTheDocument();

    const third = within(row("Latitude laptops"));
    expect(third.getByText("Off")).toBeInTheDocument();
    expect(third.getByText("1 problem")).toBeInTheDocument();
    expect(
      third.getByText(
        "Matches machines where Manufacturer equals Dell Inc. and Model starts with Latitude.",
      ),
    ).toBeInTheDocument();
  });

  it("moves a rule down from its menu, showing the new order at once and taking the server's answer", async () => {
    const answer = deferred<Response>();
    const { server, queryClient } = await open([kiosk, berlin, laptops], {
      "POST /api/rules/order": () => answer.promise,
    });

    await screen.findByRole("grid", { name: "Rules in order" });
    await chooseMenuItem(actionsFor("Rule 1, Kiosk in the lobby"), "Move down");

    await waitFor(() => {
      expect(order()).toEqual(["r2", "r1", "r3"]);
    });
    expect(within(row("Berlin office")).getByText("01")).toBeInTheDocument();
    expect(server.changes().map((request) => request.body)).toEqual([
      { ruleIds: ["r2", "r1", "r3"] },
    ]);

    await act(async () => {
      answer.resolve(json(numbered([{ ...berlin, revision: 1 }, kiosk, laptops])));
      await answer.promise;
    });

    await waitFor(() => {
      expect(queryClient.getQueryData<RuleView[]>(["rules"])?.map((rule) => rule.position)).toEqual(
        [0, 1, 2],
      );
    });
    expect(order()).toEqual(["r2", "r1", "r3"]);
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("offers no move past the top or the bottom", async () => {
    await open([kiosk, berlin]);

    await screen.findByRole("grid", { name: "Rules in order" });
    press(actionsFor("Rule 1, Kiosk in the lobby"));
    const menu = await screen.findByRole("menu");

    expect(within(menu).getByRole("menuitem", { name: "Move up" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(within(menu).getByRole("menuitem", { name: "Move down" })).not.toHaveAttribute(
      "aria-disabled",
    );
  });

  it("moves a rule up with Alt and the up arrow on its row", async () => {
    const { server } = await open([kiosk, berlin, laptops], {
      "POST /api/rules/order": () => json(numbered([kiosk, laptops, berlin])),
    });

    await screen.findByRole("grid", { name: "Rules in order" });
    fireEvent.keyDown(row("Latitude laptops"), { key: "ArrowUp", altKey: true });

    await waitFor(() => {
      expect(order()).toEqual(["r1", "r3", "r2"]);
    });
    expect(server.changes().map((request) => request.body)).toEqual([
      { ruleIds: ["r1", "r3", "r2"] },
    ]);

    // At the top, Alt and the up arrow do nothing.
    fireEvent.keyDown(row("Kiosk in the lobby"), { key: "ArrowUp", altKey: true });
    expect(server.changes()).toHaveLength(1);
  });

  it("shows the list as it is now when someone changed it before a move arrived", async () => {
    const added = ruleView({ id: "r4", position: 3, name: "Virtual machines" });
    await open([kiosk, berlin, laptops], {
      "POST /api/rules/order": () => json([kiosk, berlin, laptops, added], 409),
    });

    await screen.findByRole("grid", { name: "Rules in order" });
    await chooseMenuItem(actionsFor("Rule 3, Latitude laptops"), "Move up");

    expect(
      await screen.findByText(
        "Someone changed the rules while you moved one, so the list shows them as they are now. Move the rule again if it should still go there.",
      ),
    ).toBeInTheDocument();
    expect(order()).toEqual(["r1", "r2", "r3", "r4"]);
  });

  it("puts the rules back in their order when a move fails", async () => {
    await open([kiosk, berlin, laptops], {
      "POST /api/rules/order": { status: 500, body: { title: "The database is not there." } },
    });

    await screen.findByRole("grid", { name: "Rules in order" });
    await chooseMenuItem(actionsFor("Rule 1, Kiosk in the lobby"), "Move down");

    expect(
      await screen.findByText("The rule could not be moved: The database is not there."),
    ).toBeInTheDocument();
    expect(order()).toEqual(["r1", "r2", "r3"]);
  });

  it("adds a rule in its drawer and keeps it open with the problems it was saved with, each at its field", async () => {
    const saved: SaveRuleRequest[] = [];
    const created = ruleView({
      id: "r4",
      position: 3,
      name: "Lab machines",
      when: null,
      sequenceId: "s1",
      sequenceName: "Install Windows",
      values: [{ name: "ddtZone", value: "Lab" }],
      roleIds: ["role1"],
      problems: [
        problem("values[0].name", "Names that start with ddt are DDT's own. Choose another name."),
      ],
    });
    const { server } = await open([kiosk, berlin, laptops], {
      "POST /api/rules": (request) => {
        saved.push(request.body as SaveRuleRequest);

        return json(created, 201);
      },
    });

    press(await screen.findByRole("button", { name: "Add rule" }));
    const drawer = await screen.findByRole("dialog", { name: "Rule 4 New rule" });

    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Lab machines");
    await chooseOption(drawer, "Chooses a task sequence", "Install Windows");
    press(within(drawer).getByRole("button", { name: "Add a value" }));
    fill(within(drawer).getByRole("textbox", { name: "Name of value 1" }), "ddtZone");
    fill(within(drawer).getByRole("combobox", { name: "Value of ddtZone" }), "Lab");
    await chooseMenuItem(within(drawer).getByRole("button", { name: "Add a role" }), "Office PC");
    press(within(drawer).getByRole("button", { name: "Save rule" }));

    await within(drawer).findByText(
      "This rule has 1 problem, shown at its field. It matches no machine until it is fixed.",
    );
    expect(saved).toEqual([
      {
        revision: 0,
        name: "Lab machines",
        description: null,
        enabled: true,
        when: null,
        sequenceId: "s1",
        values: [{ name: "ddtZone", value: "Lab" }],
        roleIds: ["role1"],
      },
    ]);
    expect(
      within(drawer).getByRole("textbox", { name: "Name of value 1" }),
    ).toHaveAccessibleDescription("Names that start with ddt are DDT's own. Choose another name.");
    expect(screen.getByRole("dialog", { name: "Rule 4 Lab machines" })).toBe(drawer);
    expect(order()).toEqual(["r1", "r2", "r3", "r4"]);
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("shows a rule's problems at their fields when its drawer opens, the condition's among them", async () => {
    await open([
      kiosk,
      {
        ...berlin,
        problems: [
          problem("when.value", "Enter an IPv4 address, such as 10.0.0.1."),
          problem(
            "roleIds[0]",
            "A machine role this rule gives no longer exists. Take it out of the rule.",
          ),
        ],
        roleIds: ["gone"],
      },
    ]);

    press(await screen.findByRole("row", { name: /Berlin office/ }));
    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });

    expect(
      within(drawer).getByRole("textbox", { name: "Value of condition 1" }),
    ).toHaveAccessibleDescription("Enter an IPv4 address, such as 10.0.0.1.");
    expect(within(drawer).getByText("A machine role that is gone")).toBeInTheDocument();
    expect(
      within(drawer).getByText(
        "A machine role this rule gives no longer exists. Take it out of the rule.",
      ),
    ).toBeInTheDocument();
  });

  it("shows the server's refusal of a save at its field", async () => {
    await open([kiosk, berlin], {
      "PUT /api/rules/r2": {
        status: 400,
        body: {
          title: "One or more validation errors occurred.",
          errors: { name: ["A name has at most 100 characters and no control characters."] },
        },
      },
    });

    press(await screen.findByRole("row", { name: /Berlin office/ }));
    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });
    press(within(drawer).getByRole("button", { name: "Save rule" }));

    await waitFor(() => {
      expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveAccessibleDescription(
        "A name has at most 100 characters and no control characters.",
      );
    });
  });

  it("offers theirs or mine when someone saved the rule meanwhile, and saves mine over theirs", async () => {
    const theirs = { ...berlin, name: "Berlin and Potsdam", revision: 2, updatedBy: "bob" };
    const sent: SaveRuleRequest[] = [];
    await open([kiosk, berlin], {
      "PUT /api/rules/r2": (request) => {
        const body = request.body as SaveRuleRequest;
        sent.push(body);

        return body.revision === 1
          ? json(theirs, 409)
          : json({ ...berlin, name: body.name, revision: 3 });
      },
    });

    press(await screen.findByRole("row", { name: /Berlin office/ }));
    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });
    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Berlin");
    press(within(drawer).getByRole("button", { name: "Save rule" }));

    expect(
      await within(drawer).findByText("bob saved this rule while you were editing it."),
    ).toBeInTheDocument();
    expect(await screen.findByText("Berlin and Potsdam")).toBeInTheDocument();

    press(within(drawer).getByRole("button", { name: "Keep mine" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(sent.map((body) => [body.revision, body.name])).toEqual([
      [1, "Berlin"],
      [2, "Berlin"],
    ]);
    expect(within(list()).getByText("Berlin")).toBeInTheDocument();
  });

  it("deletes a rule after asking, and takes the list the server answers", async () => {
    const { server } = await open([kiosk, berlin, laptops], {
      "DELETE /api/rules/r1": () => json(numbered([berlin, laptops])),
    });

    await screen.findByRole("grid", { name: "Rules in order" });
    await chooseMenuItem(actionsFor("Rule 1, Kiosk in the lobby"), "Delete");
    const dialog = await screen.findByRole("dialog", { name: "Delete Kiosk in the lobby?" });
    expect(dialog).toHaveAccessibleDescription(
      "The machines it matches no longer get what it chooses, sets or gives; a rule below it may give them that instead. The rules below it move up a place. Runs that already started keep their values.",
    );

    press(within(dialog).getByRole("button", { name: "Delete rule" }));

    await waitFor(() => {
      expect(order()).toEqual(["r2", "r3"]);
    });
    expect(within(row("Berlin office")).getByText("01")).toBeInTheDocument();
    expect(server.count("GET /api/rules")).toBe(1);
  });

  it("tests a machine: the rules that match it, the sequence, each value with where it came from, and that it still needs an approval", async () => {
    const resolution: MachineSequenceResolution = {
      source: "Rule",
      sequenceId: "s1",
      sequenceName: "Install Windows",
      ruleId: "r3",
      problemCount: 0,
      explanation: "Rule 3, Latitude laptops, chooses Install Windows.",
      matchedRuleIds: ["r2", "r3"],
      values: [
        {
          name: "TimeZone",
          value: "W. Europe Standard Time",
          source: "Rule",
          sourceId: "r2",
          sourceName: "Berlin office",
          overridden: false,
        },
        {
          name: "TimeZone",
          value: "GMT Standard Time",
          source: "Rule",
          sourceId: "r3",
          sourceName: "Latitude laptops",
          overridden: true,
        },
        {
          name: "ComputerName",
          value: "PC-042",
          source: "Role",
          sourceId: "role1",
          sourceName: "Office PC",
          overridden: false,
        },
      ],
      valueProblems: [
        problem(
          "OrganizationalUnit",
          "OrganizationalUnit cannot be worked out, because it is made from itself.",
        ),
      ],
    };
    const { queryClient } = await open([kiosk, berlin, laptops], {
      "GET /api/machines/m1/sequence": { body: resolution },
    });

    const heading = await screen.findByRole("heading", { name: "Test a machine" });
    const panel = heading.closest<HTMLElement>("section");

    if (panel === null) {
      throw new Error("The test panel is not a section.");
    }

    expect(
      within(panel).getByText(/Choose a machine to see which rules match it/),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(queryClient.getQueryData(["machines"])).toHaveLength(1);
    });
    press(within(panel).getByRole("button", { name: /Show suggestions/ }));
    press(await screen.findByRole("option", { name: "PC-042, Dell Inc. Latitude 7450" }));

    expect(
      await within(panel).findByText("Rule 2, Berlin office and rule 3, Latitude laptops"),
    ).toBeInTheDocument();
    expect(within(panel).getByText("Install Windows, from rule 3")).toBeInTheDocument();
    expect(within(panel).getByText("W. Europe Standard Time, from rule 2")).toBeInTheDocument();
    expect(
      within(panel).getByText("Also set by rule 3, but rule 2 comes first."),
    ).toBeInTheDocument();
    expect(within(panel).getByText("PC-042, from the machine role Office PC")).toBeInTheDocument();
    expect(
      within(panel).getByText(
        "OrganizationalUnit cannot be worked out, because it is made from itself.",
      ),
    ).toBeInTheDocument();
    expect(
      within(panel).getByText(
        "An operator still approves this machine, or someone signs in at it; the rules only choose what then runs.",
      ),
    ).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("opens the rule the address names", async () => {
    await open([kiosk, berlin], {}, administrator, "/deployment/rules?rule=r2");

    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });
    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveValue("Berlin office");
  });

  it("shows a viewer the rules without a way to change them, and a rule's drawer to read", async () => {
    await open([kiosk, berlin], {}, viewer);

    await screen.findByRole("grid", { name: "Rules in order" });
    expect(screen.queryByRole("button", { name: "Add rule" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Move/ })).not.toBeInTheDocument();

    press(row("Berlin office"));
    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });

    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveAttribute("readonly");
    expect(within(drawer).queryByRole("button", { name: "Save rule" })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole("button", { name: "Add a value" })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole("button", { name: "Delete rule" })).not.toBeInTheDocument();
    expect(within(drawer).getAllByRole("button", { name: "Close" })).toHaveLength(2);
    await expectNoAxeViolations();
  });

  it("tells an operator without rules that an administrator adds them", async () => {
    await open([], {}, operator);

    expect(
      await screen.findByText(
        "Without rules an operator chooses the sequence of each machine. An administrator adds rules here.",
      ),
    ).toBeInTheDocument();
  });

  it("shows another administrator's change from the hub, flashing the rule, and keeps an open drawer's edit", async () => {
    const { server, hub } = await open([kiosk, berlin]);

    press(await screen.findByRole("row", { name: /Berlin office/ }));
    const drawer = await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });
    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Berlin, room 4");

    act(() => {
      hub?.push("rulesChanged", [
        kiosk,
        { ...berlin, name: "Berlin and Potsdam", revision: 2, updatedBy: "bob" },
      ]);
    });

    expect(await within(list()).findByText("Berlin and Potsdam")).toBeInTheDocument();
    expect(row("Berlin and Potsdam")).toHaveClass("live-flash");
    expect(row("Kiosk in the lobby")).not.toHaveClass("live-flash");
    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveValue("Berlin, room 4");
    expect(server.count("GET /api/rules")).toBe(1);
  });

  describe("accessibility", () => {
    it("has no violations with the rules listed", async () => {
      await open([kiosk, berlin, laptops]);

      await screen.findByRole("grid", { name: "Rules in order" });
      await expectNoAxeViolations();
    });

    it("has no violations with a rule's menu and then its drawer open", async () => {
      await open([kiosk, berlin, laptops]);

      await screen.findByRole("grid", { name: "Rules in order" });
      press(actionsFor("Rule 2, Berlin office"));
      const menu = await screen.findByRole("menu");
      await expectNoAxeViolations();

      press(within(menu).getByRole("menuitem", { name: "Change" }));
      await screen.findByRole("dialog", { name: "Rule 2 Berlin office" });
      await expectNoAxeViolations();
    });
  });
});

function problem(field: string, message = "Choose a fact or a value to test.") {
  return { stepId: null, field, message };
}
