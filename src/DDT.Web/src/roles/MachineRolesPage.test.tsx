// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { MachineRoleView, SaveMachineRoleRequest } from "@/roles/roles";
import type { RuleView } from "@/rules/rules";
import { chooseMenuItem, fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator, machineRole, operator, ruleView } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const kiosk = machineRole({
  id: "role1",
  name: "Kiosk",
  description: "The screens in the lobby.",
  values: [
    { name: "ComputerName", value: "KIOSK-{{SerialNumber}}" },
    { name: "TimeZone", value: "W. Europe Standard Time" },
  ],
  ruleCount: 1,
});

const office = machineRole({ id: "role2", name: "Office PC", values: [] });

const lobby = ruleView({ id: "r1", position: 0, name: "Kiosk in the lobby", roleIds: ["role1"] });
const berlin = ruleView({ id: "r2", position: 1, name: "Berlin office", roleIds: [] });

function open(
  roles: MachineRoleView[],
  routes: Routes = {},
  user = administrator,
  rules: RuleView[] = [lobby, berlin],
): Promise<RenderedPage> {
  return renderPage({
    path: "/deployment/machine-roles",
    user,
    routes: {
      "GET /api/machine-roles": { body: roles },
      "GET /api/rules": { body: rules },
      "GET /api/sequences/facts": { body: [] },
      "GET /api/accounts": { body: [] },
      ...routes,
    },
  });
}

function table(): HTMLElement {
  return screen.getByRole("grid", { name: "Machine roles", hidden: true });
}

function row(name: string): HTMLElement {
  return within(table()).getByRole("row", { name: new RegExp(`^${name}`), hidden: true });
}

describe("MachineRolesPage", () => {
  it("says what a machine role is, and what to do without any", async () => {
    await open([]);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Machine roles" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/It says nothing about what a person may do; that is a user's role/),
    ).toBeInTheDocument();
    expect(await screen.findByText("No machine roles yet")).toBeInTheDocument();
  });

  it("lists the roles with their values and the rules that give them, each a link to the rule", async () => {
    await open([kiosk, office]);

    await screen.findByRole("grid", { name: "Machine roles" });
    const first = within(row("Kiosk"));

    expect(first.getByText("The screens in the lobby.")).toBeInTheDocument();
    expect(first.getByText("2 values")).toBeInTheDocument();
    expect(first.getByText("ComputerName, TimeZone")).toBeInTheDocument();
    expect(first.getByRole("link", { name: "Rule 1, Kiosk in the lobby" })).toHaveAttribute(
      "href",
      "/deployment/rules?rule=r1",
    );
    expect(within(row("Office PC")).getByText("No values")).toBeInTheDocument();
    expect(within(row("Office PC")).getByText("No rule")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("adds a role in its drawer and shows the server's answer without reading the list again", async () => {
    const sent: SaveMachineRoleRequest[] = [];
    const { server } = await open([kiosk], {
      "POST /api/machine-roles": (request) => {
        const body = request.body as SaveMachineRoleRequest;
        sent.push(body);

        return json(machineRole({ id: "role3", name: body.name, values: body.values }), 201);
      },
    });

    press(await screen.findByRole("button", { name: "Add machine role" }));
    const drawer = await screen.findByRole("dialog", { name: "Machine role New machine role" });
    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Lab machine");
    press(within(drawer).getByRole("button", { name: "Add a value" }));
    fill(within(drawer).getByRole("textbox", { name: "Name of value 1" }), "TimeZone");
    fill(within(drawer).getByRole("combobox", { name: "Value of TimeZone" }), "UTC");
    press(within(drawer).getByRole("button", { name: "Save machine role" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(sent).toEqual([
      {
        revision: 0,
        name: "Lab machine",
        description: null,
        values: [{ name: "TimeZone", value: "UTC" }],
      },
    ]);
    expect(within(row("Lab machine")).getByText("1 value")).toBeInTheDocument();
    expect(server.count("GET /api/machine-roles")).toBe(1);
  });

  it("shows a refused value at its own row, past a blank row that was left out", async () => {
    const { server } = await open([kiosk], {
      "PUT /api/machine-roles/role1": {
        status: 400,
        body: {
          title: "One or more validation errors occurred.",
          errors: {
            "values[2].name": ["Names that start with ddt are DDT's own. Choose another name."],
          },
        },
      },
    });

    press(await screen.findByRole("row", { name: /^Kiosk/ }));
    const drawer = await screen.findByRole("dialog", { name: "Machine role Kiosk" });
    press(within(drawer).getByRole("button", { name: "Add a value" }));
    press(within(drawer).getByRole("button", { name: "Add a value" }));
    fill(within(drawer).getByRole("textbox", { name: "Name of value 4" }), "ddtZone");
    press(within(drawer).getByRole("button", { name: "Save machine role" }));

    await waitFor(() => {
      expect(
        within(drawer).getByRole("textbox", { name: "Name of value 4" }),
      ).toHaveAccessibleDescription(
        "Names that start with ddt are DDT's own. Choose another name.",
      );
    });
    expect(within(drawer).getByRole("textbox", { name: "Name of value 3" })).not.toHaveAttribute(
      "aria-invalid",
    );
    expect(
      (server.changes()[0]?.body as SaveMachineRoleRequest).values.map((value) => value.name),
    ).toEqual(["ComputerName", "TimeZone", "ddtZone"]);
  });

  it("names the rules that give a role and offers no deletion until they no longer do", async () => {
    await open([kiosk, office]);

    await screen.findByRole("grid", { name: "Machine roles" });
    await chooseMenuItem(
      within(row("Kiosk")).getByRole("button", { name: "Actions for Kiosk" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Kiosk?" });

    expect(dialog).toHaveTextContent(
      "Rule 1, Kiosk in the lobby gives this machine role. Take it out of that rule, then delete the role.",
    );
    expect(
      within(dialog).getByRole("link", { name: "Rule 1, Kiosk in the lobby" }),
    ).toHaveAttribute("href", "/deployment/rules?rule=r1");
    expect(within(dialog).getByRole("button", { name: "Delete machine role" })).toBeDisabled();
  });

  it("explains the server's refusal when a rule came to give the role meanwhile", async () => {
    let rules: RuleView[] = [lobby, berlin];
    const { server } = await open([kiosk, office], {
      "GET /api/rules": () => json(rules),
      "DELETE /api/machine-roles/role2": () => {
        rules = [lobby, { ...berlin, roleIds: ["role2"], revision: 2 }];

        return json(
          {
            title: "A rule gives this machine role. Take it out of the rule, then delete the role.",
            code: "machineRole.givenByRules",
            args: { count: 1 },
          },
          409,
        );
      },
    });

    await screen.findByRole("grid", { name: "Machine roles" });
    await chooseMenuItem(
      within(row("Office PC")).getByRole("button", { name: "Actions for Office PC" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Office PC?" });
    expect(dialog).toHaveTextContent(
      "No rule gives Office PC, so deleting it changes the values of no machine.",
    );

    press(within(dialog).getByRole("button", { name: "Delete machine role" }));

    await waitFor(() => {
      expect(dialog).toHaveTextContent(
        "Rule 2, Berlin office gives this machine role. Take it out of that rule, then delete the role.",
      );
    });
    expect(within(dialog).getByRole("button", { name: "Delete machine role" })).toBeDisabled();
    expect(server.count("GET /api/rules")).toBe(2);
  });

  it("deletes a role no rule gives", async () => {
    const { server } = await open([kiosk, office], {
      "DELETE /api/machine-roles/role2": { status: 204 },
    });

    await screen.findByRole("grid", { name: "Machine roles" });
    await chooseMenuItem(
      within(row("Office PC")).getByRole("button", { name: "Actions for Office PC" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Office PC?" });
    press(within(dialog).getByRole("button", { name: "Delete machine role" }));

    await waitFor(() => {
      expect(within(table()).queryByRole("row", { name: /^Office PC/, hidden: true })).toBeNull();
    });
    expect(server.count("GET /api/machine-roles")).toBe(1);
  });

  it("shows an operator the roles without changes, and a role's values to read", async () => {
    await open([kiosk], {}, operator);

    await screen.findByRole("grid", { name: "Machine roles" });
    expect(screen.queryByRole("button", { name: "Add machine role" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();

    press(row("Kiosk"));
    const drawer = await screen.findByRole("dialog", { name: "Machine role Kiosk" });

    expect(within(drawer).getByRole("textbox", { name: "Name of value 1" })).toHaveAttribute(
      "readonly",
    );
    expect(within(drawer).queryByRole("button", { name: "Save machine role" })).toBeNull();
    expect(within(drawer).queryByRole("button", { name: /^Remove/ })).toBeNull();
    await expectNoAxeViolations();
  });

  it("takes another administrator's change from the hub and points it out", async () => {
    const { server, hub } = await open([kiosk, office]);

    await screen.findByRole("grid", { name: "Machine roles" });
    act(() => {
      hub?.push("rolesChanged", [
        kiosk,
        { ...office, values: [{ name: "TimeZone", value: "UTC" }], revision: 2 },
      ]);
    });

    expect(await within(row("Office PC")).findByText("1 value")).toBeInTheDocument();
    expect(row("Office PC")).toHaveClass("live-flash");
    expect(row("Kiosk")).not.toHaveClass("live-flash");
    expect(server.count("GET /api/machine-roles")).toBe(1);
  });

  it("has no violations with a role's drawer open", async () => {
    await open([kiosk]);

    press(await screen.findByRole("row", { name: /^Kiosk/ }));
    await screen.findByRole("dialog", { name: "Machine role Kiosk" });
    await expectNoAxeViolations();
  });
});
