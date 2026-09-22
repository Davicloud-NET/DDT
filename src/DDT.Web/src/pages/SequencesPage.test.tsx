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
  useParams,
} from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { DeploymentSummary } from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";
import type { AssignmentRuleView } from "@/rules/rules";
import type {
  CreateSequenceRequest,
  SequenceSummary,
  SequenceTemplate,
  SequenceView,
} from "@/sequences/sequences";

import { SequencesPage } from "./SequencesPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

const installId = "0193a4b2-0000-7000-8000-0000000000e1";

function sequence(overrides: Partial<SequenceSummary>): SequenceSummary {
  return {
    id: installId,
    name: "Install Windows",
    description: null,
    revision: 3,
    stepCount: 4,
    problemCount: 0,
    warningCount: 0,
    erasesDisk: true,
    needsComputerName: false,
    continuesInWindows: false,
    updatedUtc: new Date(Date.now() - 120_000).toISOString(),
    updatedBy: "admin",
    ...overrides,
  };
}

function rule(overrides: Partial<AssignmentRuleView>): AssignmentRuleView {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000f1",
    kind: "Model",
    mac: null,
    manufacturer: null,
    model: "Latitude 7440",
    sequenceId: installId,
    sequenceName: "Install Windows",
    description: null,
    updatedUtc: "2026-09-15T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

const template: SequenceTemplate = {
  key: "install-windows",
  name: "Install Windows",
  description: "Partitions the disk, applies an image and writes the answer file.",
  definition: {
    version: 1,
    steps: [
      {
        kind: "partition",
        id: "00000000-0000-4000-8000-000000000001",
        name: "Partition the disk",
        conditions: [],
        continueOnError: false,
        rebootAfter: false,
        systemPartitionMegabytes: 300,
        recoveryPartitionMegabytes: 1024,
      },
    ],
  },
};

function view(request: CreateSequenceRequest, id: string): SequenceView {
  return {
    id,
    name: request.name,
    description: request.description,
    revision: 1,
    definition: request.definition,
    stepPhases: request.definition.steps.map(() => "WindowsPE"),
    problems: [],
    warnings: [],
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
  };
}

function runningMachine(sequenceId: string): MachineSummary {
  const deployment = { state: "Running", sequenceId } as DeploymentSummary;

  return { id: crypto.randomUUID(), deployment } as MachineSummary;
}

interface Sent {
  method: string;
  path: string;
  body: unknown;
}

type Handler = (request: Sent) => Response;

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

function EditorStub() {
  const { sequenceId } = useParams({ strict: false });

  return <p>{`Editing ${sequenceId ?? ""}`}</p>;
}

// Answers "METHOD path" from the handlers, the current user and the CSRF token; everything else is a 404.
function serve(user: CurrentUser, handlers: Record<string, Handler>) {
  const requests: Sent[] = [];

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

      const handler = handlers[`${request.method} ${request.path}`];

      return Promise.resolve(
        handler === undefined ? new Response(null, { status: 404 }) : handler(request),
      );
    }),
  );

  const rootRoute = createRootRoute();
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sequences",
        component: SequencesPage,
      }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sequences/$sequenceId",
        component: EditorStub,
      }),
    ]),
    history: createMemoryHistory({ initialEntries: ["/sequences"] }),
  });

  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return requests;
}

describe("SequencesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells an administrator to start from the template when there is no sequence", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([]),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () => json([]),
    });

    expect(await screen.findByRole("heading", { name: "No sequences yet" })).toBeInTheDocument();
    expect(screen.getByText(/Start from the Install Windows template/)).toBeInTheDocument();
    expect(
      await screen.findByRole("button", { name: "New from the Install Windows template" }),
    ).toBeInTheDocument();
  });

  it("offers a viewer no changes", async () => {
    serve(viewer, {
      "GET /api/sequences": () => json([sequence({})]),
      "GET /api/rules": () => json([]),
    });

    expect(await screen.findByRole("link", { name: "Install Windows" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /New/ })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Delete Install Windows" }),
    ).not.toBeInTheDocument();
  });

  it("tells a viewer that an administrator creates the sequences", async () => {
    serve(viewer, {
      "GET /api/sequences": () => json([]),
      "GET /api/rules": () => json([]),
    });

    expect(
      await screen.findByText(
        "An administrator creates sequences here. Until then, no machine can be given one.",
      ),
    ).toBeInTheDocument();
  });

  it("lists each sequence with its steps, problems, the rules that choose it and its last change", async () => {
    serve(administrator, {
      "GET /api/sequences": () =>
        json([
          sequence({ continuesInWindows: true, needsComputerName: true }),
          sequence({
            id: "0193a4b2-0000-7000-8000-0000000000e2",
            name: "Lab draft",
            description: "Scripts for the lab",
            stepCount: 1,
            problemCount: 2,
            warningCount: 1,
            erasesDisk: false,
          }),
        ]),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () =>
        json([rule({}), rule({ id: "r2", kind: "Mac", mac: "00155D010203", model: null })]),
    });

    const install = (await screen.findByRole("link", { name: "Install Windows" })).closest("tr");
    const draft = screen.getByRole("link", { name: "Lab draft" }).closest("tr");

    if (install === null || draft === null) {
      throw new Error("The sequences are not in table rows.");
    }

    expect(install).toHaveTextContent("4 steps");
    expect(install).toHaveTextContent("Erases the disk, Continues in Windows, Joins the domain");
    expect(install).toHaveTextContent("Ready to run");
    expect(within(install).getByText("model Latitude 7440")).toBeInTheDocument();
    expect(within(install).getByText("MAC 00:15:5D:01:02:03")).toBeInTheDocument();
    expect(install).toHaveTextContent("2 minutes ago");
    expect(install).toHaveTextContent("by admin");

    expect(draft).toHaveTextContent("Scripts for the lab");
    expect(draft).toHaveTextContent("Keeps the disk");
    expect(draft).toHaveTextContent("2 problems, cannot run");
    expect(draft).toHaveTextContent("1 warning");
    expect(draft).toHaveTextContent("No rule");
  });

  it("creates a sequence from the template with new step ids and a free name, then opens it", async () => {
    let created: CreateSequenceRequest | null = null;
    const newId = "0193a4b2-0000-7000-8000-0000000000e9";

    serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () => json([]),
      "POST /api/sequences": (request) => {
        created = request.body as CreateSequenceRequest;
        return json(view(created, newId), 201);
      },
    });

    fireEvent.click(
      await screen.findByRole("button", { name: "New from the Install Windows template" }),
    );

    expect(await screen.findByText(`Editing ${newId}`)).toBeInTheDocument();

    const request = created as CreateSequenceRequest | null;
    expect(request?.name).toBe("Install Windows 2");
    expect(request?.description).toBe(template.description);
    expect(request?.definition.steps.map((step) => step.name)).toEqual(["Partition the disk"]);
    expect(request?.definition.steps[0]?.id).not.toBe(template.definition.steps[0]?.id);
  });

  it("creates an empty sequence", async () => {
    let created: CreateSequenceRequest | null = null;

    serve(administrator, {
      "GET /api/sequences": () => json([]),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () => json([]),
      "POST /api/sequences": (request) => {
        created = request.body as CreateSequenceRequest;
        return json(view(created, "e0"), 201);
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "New empty sequence" }));

    expect(await screen.findByText("Editing e0")).toBeInTheDocument();
    expect(created).toEqual({
      name: "New sequence",
      description: null,
      definition: { version: 1, steps: [] },
    });
  });

  it("names the rules and the machines a deletion concerns, and shows the server's refusal in place", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () => json([rule({})]),
      "GET /api/machines": () => json([runningMachine(installId), runningMachine("other")]),
      [`DELETE /api/sequences/${installId}`]: () =>
        json(
          {
            title:
              "A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.",
          },
          409,
        ),
    });

    await screen.findByText("model Latitude 7440");
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Delete Install Windows" })).toBeEnabled();
    });
    fireEvent.click(screen.getByRole("button", { name: "Delete Install Windows" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Install Windows?" });
    await waitFor(() => {
      expect(dialog).toHaveTextContent(
        "Install Windows is deleted and can no longer be assigned or chosen at a machine. The rule for model Latitude 7440 chooses it, so the server keeps it until that rule is deleted or chooses another sequence. The machine it is assigned to or running on keeps the copy it got. Runs that already ended keep their history.",
      );
    });

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete sequence" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.",
    );
  });

  it("deletes a sequence no rule chooses", async () => {
    let list = [sequence({})];

    const requests = serve(administrator, {
      "GET /api/sequences": () => json(list),
      "GET /api/sequences/templates": () => json([template]),
      "GET /api/rules": () => json([]),
      [`DELETE /api/sequences/${installId}`]: () => {
        list = [];
        return new Response(null, { status: 204 });
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Delete Install Windows" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete sequence" }));

    expect(await screen.findByRole("heading", { name: "No sequences yet" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === "DELETE")).toBe(true);
  });
});
