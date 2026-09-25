// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { HardwareModelCount } from "@/machines/machines";
import type { AssignmentRuleView, SaveAssignmentRuleRequest } from "@/rules/rules";
import type { SequenceSummary } from "@/sequences/sequences";
import { machineSummary } from "@/test/builders";

import { RulesPage } from "./RulesPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

const operator: CurrentUser = { ...administrator, userName: "operator", roles: ["Operator"] };

function sequence(id: string, name: string, problemCount = 0): SequenceSummary {
  return {
    id,
    name,
    description: null,
    revision: 1,
    stepCount: 4,
    problemCount,
    warningCount: 0,
    erasesDisk: true,
    needsComputerName: false,
    continuesInWindows: false,
    updatedUtc: "2026-09-15T10:00:00Z",
    updatedBy: "admin",
    rawImageName: null,
    rawImageBootCapability: null,
  };
}

const sequences = [
  sequence("s1", "Install Windows"),
  sequence("s2", "Lab PCs"),
  sequence("s3", "Draft", 2),
];

function rule(overrides: Partial<AssignmentRuleView>): AssignmentRuleView {
  return {
    id: "r1",
    kind: "Model",
    mac: null,
    manufacturer: "Dell Inc.",
    model: "Latitude*",
    sequenceId: "s1",
    sequenceName: "Install Windows",
    description: null,
    updatedUtc: new Date(Date.now() - 120_000).toISOString(),
    updatedBy: "admin",
    ...overrides,
  };
}

const macRule = rule({
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

const machine = machineSummary({ id: "m1" });

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

interface Sent {
  method: string;
  path: string;
  body: unknown;
}

type Handler = (request: Sent) => Response;

function serve(
  user: CurrentUser,
  rules: () => AssignmentRuleView[],
  handlers: Record<string, Handler> = {},
) {
  const requests: Sent[] = [];
  const defaults: Record<string, Handler> = {
    "GET /api/rules": () => json(rules()),
    "GET /api/sequences": () => json(sequences),
    "GET /api/machines": () => json([machine]),
    "GET /api/machines/models": () => json(models),
  };

  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const request: Sent = {
        method: init?.method ?? "GET",
        path: url.replace("http://localhost", ""),
        body: typeof init?.body === "string" ? JSON.parse(init.body) : null,
      };

      requests.push(request);

      if (request.path === "/api/auth/me") {
        return Promise.resolve(json(user));
      }

      if (request.path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      const key = `${request.method} ${request.path}`;
      const handler = handlers[key] ?? defaults[key];

      return Promise.resolve(
        handler === undefined ? new Response(null, { status: 404 }) : handler(request),
      );
    }),
  );

  const rootRoute = createRootRoute();
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({ getParentRoute: () => rootRoute, path: "/rules", component: RulesPage }),
    ]),
    history: createMemoryHistory({ initialEntries: ["/rules"] }),
  });

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return { requests, queryClient };
}

function ruleItem(description: string): HTMLElement {
  const group = screen.getByRole("group", { name: `The rule for ${description}` });
  const item = group.closest("li");

  if (item === null) {
    throw new Error(`The rule for ${description} is not in the list.`);
  }

  return item;
}

describe("RulesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("says that rules only choose and never authorize, and what to do without any", async () => {
    serve(administrator, () => []);

    expect(
      await screen.findByText(
        "A rule chooses the sequence for a machine that has none. It never authorizes a machine: an operator still approves it on the Machines page, or someone signs in at it.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/comes before every rule/)).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "No rules" })).toBeInTheDocument();
    expect(
      screen.getByText(/Add a rule to choose one by MAC address or hardware model\./),
    ).toBeInTheDocument();
  });

  it("lists the rules by kind with their sequence and the machines they match", async () => {
    serve(administrator, () => [rule({}), macRule]);

    await screen.findByRole("heading", { name: "Rules by model" });
    const model = ruleItem("model Dell Inc. Latitude*");
    const mac = ruleItem("MAC 00:15:5D:01:02:03");

    expect(within(model).getByLabelText("Model")).toHaveValue("Latitude*");
    expect(within(model).getByLabelText("Sequence")).toHaveValue("s1");
    expect(model).toHaveTextContent("Matches 5 machines");
    expect(model).toHaveTextContent(/Changed 2 minutes ago by admin/);

    expect(within(mac).getByLabelText("MAC address")).toHaveValue("00:15:5D:01:02:03");
    expect(mac).toHaveTextContent("Matches 1 machine");

    const draft = within(model).getByRole("option", { name: "Draft (2 problems, cannot run)" });
    expect(draft).toBeDisabled();
  });

  it("saves a rule in place as it changes", async () => {
    const saves: SaveAssignmentRuleRequest[] = [];

    serve(administrator, () => [rule({})], {
      "PUT /api/rules/r1": (request) => {
        const body = request.body as SaveAssignmentRuleRequest;
        saves.push(body);
        return json(rule({ sequenceId: body.sequenceId, sequenceName: "Lab PCs" }));
      },
    });

    await screen.findByRole("heading", { name: "Rules by model" });
    fireEvent.change(within(ruleItem("model Dell Inc. Latitude*")).getByLabelText("Sequence"), {
      target: { value: "s2" },
    });

    await waitFor(() => {
      expect(saves).toEqual([
        {
          kind: "Model",
          mac: null,
          manufacturer: "Dell Inc.",
          model: "Latitude*",
          sequenceId: "s2",
          description: null,
        },
      ]);
    });
    expect(
      await within(ruleItem("model Dell Inc. Latitude*")).findByText("Saved"),
    ).toBeInTheDocument();
  });

  it("shows another administrator's change in a row with nothing unsaved, and keeps a row's own edit", async () => {
    const { queryClient } = serve(administrator, () => [rule({}), macRule]);

    await screen.findByRole("heading", { name: "Rules by model" });
    fireEvent.change(within(ruleItem("MAC 00:15:5D:01:02:03")).getByLabelText("Description"), {
      target: { value: "Room 4" },
    });

    act(() => {
      queryClient.setQueryData(
        ["rules"],
        [
          rule({ sequenceId: "s2", sequenceName: "Lab PCs", updatedBy: "bob" }),
          { ...macRule, description: "Lab bench", updatedBy: "bob" },
        ],
      );
    });

    await waitFor(() => {
      expect(within(ruleItem("model Dell Inc. Latitude*")).getByLabelText("Sequence")).toHaveValue(
        "s2",
      );
    });
    expect(within(ruleItem("MAC 00:15:5D:01:02:03")).getByLabelText("Description")).toHaveValue(
      "Room 4",
    );
  });

  it("adds a new rule only with Add, and shows a duplicate at its MAC address", async () => {
    let list: AssignmentRuleView[] = [macRule];
    const created: SaveAssignmentRuleRequest[] = [];

    serve(administrator, () => list, {
      "POST /api/rules": (request) => {
        const body = request.body as SaveAssignmentRuleRequest;
        created.push(body);

        if (created.length === 1) {
          return json(
            {
              title:
                "There is a rule for MAC 00:15:5D:01:02:03 already. It chooses Lab PCs; change that rule instead.",
            },
            409,
          );
        }

        const added = rule({
          id: "r3",
          kind: "Mac",
          mac: "00155D0A0B0C",
          model: null,
          manufacturer: null,
        });
        list = [...list, added];
        return json(added, 201);
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Add MAC rule" }));
    const form = screen.getByRole("region", { name: "New rule by MAC address" });

    fireEvent.change(within(form).getByLabelText("MAC address"), {
      target: { value: "00-15-5D-01-02-03" },
    });
    expect(within(form).getByLabelText("Sequence")).toHaveValue("s1");
    await new Promise((resolve) => setTimeout(resolve, 900));
    expect(created).toHaveLength(0);

    fireEvent.click(within(form).getByRole("button", { name: "Add" }));

    await waitFor(() => {
      expect(within(form).getByLabelText("MAC address")).toHaveAccessibleDescription(
        "There is a rule for MAC 00:15:5D:01:02:03 already. It chooses Lab PCs; change that rule instead.",
      );
    });
    expect(created[0]).toEqual({
      kind: "Mac",
      mac: "00-15-5D-01-02-03",
      manufacturer: null,
      model: null,
      sequenceId: "s1",
      description: null,
    });

    fireEvent.change(within(form).getByLabelText("MAC address"), {
      target: { value: "00:15:5D:0A:0B:0C" },
    });
    fireEvent.click(within(form).getByRole("button", { name: "Add" }));

    expect(
      await screen.findByRole("group", { name: "The rule for MAC 00:15:5D:0A:0B:0C" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "New rule by MAC address" })).toBeNull();
  });

  it("says what deleting a rule changes", async () => {
    let list = [rule({})];

    serve(administrator, () => list, {
      "DELETE /api/rules/r1": () => {
        list = [];
        return new Response(null, { status: 204 });
      },
    });

    fireEvent.click(
      await screen.findByRole("button", { name: "Delete the rule for model Dell Inc. Latitude*" }),
    );

    const dialog = await screen.findByRole("dialog", {
      name: "Delete the rule for model Dell Inc. Latitude*?",
    });
    expect(dialog).toHaveTextContent(
      "Machines of model Dell Inc. Latitude* no longer get Install Windows chosen for them; another rule or an operator chooses instead. Machines that already have a run keep it.",
    );

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete rule" }));

    expect(await screen.findByRole("heading", { name: "No rules" })).toBeInTheDocument();
  });

  it("shows an operator the rules without changes", async () => {
    serve(operator, () => [macRule]);

    const mac = await screen.findByRole("group", { name: "The rule for MAC 00:15:5D:01:02:03" });

    expect(within(mac).getByLabelText("MAC address")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Add MAC rule" })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Delete/ })).toBeNull();
  });
});
