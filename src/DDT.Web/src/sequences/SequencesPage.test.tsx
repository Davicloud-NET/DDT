// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
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
import type { MachineSummary } from "@/machines/machines";
import type { RuleView } from "@/rules/rules";
import { deploymentSummary, machineSummary, ruleView } from "@/test/builders";
import { rowOf } from "@/test/rowOf";

import {
  SEQUENCE_VERSION,
  type CreateSequenceRequest,
  type SequenceSummary,
  type SequenceTemplate,
  type SequenceView,
} from "./sequences";
import { sequencesSearch } from "./sequenceSearch";
import { SequencesPage } from "./SequencesPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  mustChangePassword: false,
  roles: ["Administrator"],
};

const operator: CurrentUser = { ...administrator, userName: "operator", roles: ["Operator"] };

const installId = "0193a4b2-0000-7000-8000-0000000000e1";
const draftId = "0193a4b2-0000-7000-8000-0000000000e2";

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
    rawImageName: null,
    rawImageBootCapability: null,
    rawImageSignedUnder: null,
    ...overrides,
  };
}

const labDraft = sequence({
  id: draftId,
  name: "Lab draft",
  description: "Scripts for the lab",
  stepCount: 1,
  problemCount: 2,
  warningCount: 1,
  erasesDisk: false,
});

function rule(overrides: Partial<RuleView>): RuleView {
  return ruleView({ sequenceId: installId, ...overrides });
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
  return machineSummary({
    id: crypto.randomUUID(),
    state: "Deploying",
    deployment: deploymentSummary({ state: "Running", sequenceId }),
  });
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

// Answers "METHOD path" from the handlers, the current user and the CSRF token. Lists nobody set are empty, and
// everything else is a 404.
function serve(user: CurrentUser, handlers: Record<string, Handler>) {
  const requests: Sent[] = [];
  const empty = ["GET /api/rules", "GET /api/machines"];

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
      const handler = handlers[key];

      if (handler === undefined && empty.includes(key)) {
        return Promise.resolve(json([]));
      }

      return Promise.resolve(
        handler === undefined ? new Response(null, { status: 404 }) : handler(request),
      );
    }),
  );

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rootRoute = createRootRoute();
  const shellRoute = createRoute({ getParentRoute: () => rootRoute, id: "shell" });
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      shellRoute.addChildren([
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/sequences",
          validateSearch: sequencesSearch,
          component: SequencesPage,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/sequences/$sequenceId",
          component: EditorStub,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/rules",
          component: () => <p>The rules</p>,
        }),
      ]),
    ]),
    history: createMemoryHistory({ initialEntries: ["/deployment/sequences"] }),
  });

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { requests, queryClient };
}

// The key in the page's header; an empty list has a second one.
async function openNewSequence() {
  const [key] = await screen.findAllByRole("button", { name: "New task sequence" });

  if (key === undefined) {
    throw new Error("There is no key for a new sequence.");
  }

  fireEvent.click(key);
}

function row(name: string): HTMLElement {
  return rowOf(screen.getByRole("link", { name }));
}

describe("SequencesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells an administrator to start from a template when there is no sequence", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([]),
      "GET /api/sequences/templates": () => json([template]),
    });

    expect(await screen.findByText("No task sequences yet")).toBeInTheDocument();
    expect(screen.getByText(/^Start from a template, such as Install Windows/)).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "New task sequence" })).toHaveLength(2);
  });

  it("offers someone who is no administrator no changes", async () => {
    serve(operator, { "GET /api/sequences": () => json([sequence({})]) });

    expect(await screen.findByRole("link", { name: "Install Windows" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /New task sequence/ })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Delete Install Windows" }),
    ).not.toBeInTheDocument();
  });

  it("tells someone who is no administrator that an administrator creates the sequences", async () => {
    serve(operator, { "GET /api/sequences": () => json([]) });

    expect(
      await screen.findByText(
        "An administrator creates task sequences here. Until then, no machine can be given one.",
      ),
    ).toBeInTheDocument();
  });

  it("lists each sequence with what it does, whether it can run, what uses it and its last change", async () => {
    serve(administrator, {
      "GET /api/sequences": () =>
        json([sequence({ continuesInWindows: true, needsComputerName: true }), labDraft]),
      "GET /api/rules": () =>
        json([rule({}), rule({ id: "r2", position: 1, name: "Kiosk in the lobby" })]),
      "GET /api/machines": () => json([runningMachine(installId), runningMachine("other")]),
    });

    await screen.findByRole("link", { name: "Install Windows" });
    const install = row("Install Windows");
    const draft = row("Lab draft");

    expect(install).toHaveTextContent("4 steps");
    expect(install).toHaveTextContent(
      "Erases the disk, goes on in the installed Windows, needs a computer name",
    );
    expect(install).toHaveTextContent("Ready to run");
    await waitFor(() => {
      expect(within(install).getByText("Rule 1, Latitude laptops")).toBeInTheDocument();
    });
    expect(within(install).getByText("Rule 2, Kiosk in the lobby")).toBeInTheDocument();
    expect(install).toHaveTextContent("Assigned to or running on 1 machine");
    expect(install).toHaveTextContent("2 minutes ago");
    expect(install).toHaveTextContent("by admin");

    expect(draft).toHaveTextContent("Scripts for the lab");
    expect(draft).toHaveTextContent("Keeps the disk");
    expect(draft).toHaveTextContent("Cannot run");
    expect(draft).toHaveTextContent("2 problems, 1 warning");
    expect(draft).toHaveTextContent("No rule, no machine");
  });

  it("narrows the list by state and by what is typed", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([sequence({}), labDraft]),
    });

    await screen.findByRole("link", { name: "Install Windows" });
    fireEvent.click(screen.getByRole("radio", { name: /Cannot run/ }));

    await waitFor(() => {
      expect(screen.queryByRole("link", { name: "Install Windows" })).not.toBeInTheDocument();
    });
    expect(screen.getByRole("link", { name: "Lab draft" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("radio", { name: /All/ }));
    fireEvent.change(screen.getByRole("searchbox", { name: "Find a task sequence" }), {
      target: { value: "windows" },
    });

    await waitFor(() => {
      expect(screen.queryByRole("link", { name: "Lab draft" })).not.toBeInTheDocument();
    });
    expect(screen.getByRole("link", { name: "Install Windows" })).toBeInTheDocument();

    fireEvent.change(screen.getByRole("searchbox", { name: "Find a task sequence" }), {
      target: { value: "nothing like it" },
    });
    expect(await screen.findByText("No task sequence matches")).toBeInTheDocument();
  });

  it("creates a sequence from the template with new step ids and a free name, puts it in the list and opens it", async () => {
    let created: CreateSequenceRequest | null = null;
    const newId = "0193a4b2-0000-7000-8000-0000000000e9";

    const { queryClient } = serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
      "GET /api/sequences/templates": () => json([template]),
      "POST /api/sequences": (request) => {
        created = request.body as CreateSequenceRequest;
        return json(view(created, newId), 201);
      },
    });

    await screen.findByRole("link", { name: "Install Windows" });
    fireEvent.click(screen.getByRole("button", { name: "New task sequence" }));

    const dialog = await screen.findByRole("dialog", { name: "New task sequence" });
    await waitFor(() => {
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue(
        "Install Windows 2",
      );
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Create and open" }));

    expect(await screen.findByText(`Editing ${newId}`)).toBeInTheDocument();

    const request = created as CreateSequenceRequest | null;
    expect(request?.name).toBe("Install Windows 2");
    expect(request?.description).toBe(template.description);
    expect(request?.definition.steps.map((step) => step.name)).toEqual(["Partition the disk"]);
    expect(request?.definition.steps[0]?.id).not.toBe(template.definition.steps[0]?.id);
    // The answer is the list's entry and the editor's first copy.
    expect(
      queryClient.getQueryData<SequenceSummary[]>(["sequences"])?.map((entry) => entry.name),
    ).toEqual(["Install Windows", "Install Windows 2"]);
    expect(queryClient.getQueryData(["sequence", newId])).toMatchObject({ revision: 1 });
  });

  it("creates an empty sequence", async () => {
    let created: CreateSequenceRequest | null = null;

    serve(administrator, {
      "GET /api/sequences": () => json([]),
      "GET /api/sequences/templates": () => json([template]),
      "POST /api/sequences": (request) => {
        created = request.body as CreateSequenceRequest;
        return json(view(created, "e0"), 201);
      },
    });

    await openNewSequence();
    const dialog = await screen.findByRole("dialog", { name: "New task sequence" });
    await waitFor(() => {
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue("Install Windows");
    });

    fireEvent.click(within(dialog).getByRole("button", { name: /Start from/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Empty sequence" }));
    expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue("New sequence");
    fireEvent.click(within(dialog).getByRole("button", { name: "Create and open" }));

    expect(await screen.findByText("Editing e0")).toBeInTheDocument();
    expect(created).toEqual({
      name: "New sequence",
      description: null,
      definition: { version: SEQUENCE_VERSION, steps: [] },
    });
  });

  it("shows the server's refusal of a name at the field", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([]),
      "GET /api/sequences/templates": () => json([template]),
      "POST /api/sequences": () =>
        json(
          {
            title: "Invalid",
            errors: { name: ["Another sequence is already called Install Windows."] },
          },
          400,
        ),
    });

    await openNewSequence();
    const dialog = await screen.findByRole("dialog", { name: "New task sequence" });
    await waitFor(() => {
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveValue("Install Windows");
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Create and open" }));

    await waitFor(() => {
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveAccessibleDescription(
        /Another sequence is already called Install Windows\./,
      );
    });
  });

  it("names the rules that keep a sequence, and offers no deletion until they choose another", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
      "GET /api/rules": () => json([rule({})]),
    });

    await screen.findByRole("link", { name: "Install Windows" });
    await waitFor(() => {
      expect(screen.getByText("Rule 1, Latitude laptops")).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole("button", { name: "Delete Install Windows" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Install Windows?" });
    expect(dialog).toHaveTextContent(
      "Rule 1, Latitude laptops chooses Install Windows. Let that rule choose another sequence or none, then delete this one.",
    );
    expect(within(dialog).getByRole("button", { name: "Delete sequence" })).toBeDisabled();

    fireEvent.click(within(dialog).getByRole("link", { name: "Go to the rules" }));
    expect(await screen.findByText("The rules")).toBeInTheDocument();
  });

  it("says what a deletion does to the machines that use the sequence, and shows a refusal in place", async () => {
    serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
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

    await screen.findByRole("link", { name: "Install Windows" });
    await waitFor(() => {
      expect(screen.getByText("Assigned to or running on 1 machine")).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole("button", { name: "Delete Install Windows" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Install Windows?" });
    for (const sentence of [
      "Install Windows is deleted and can no longer be assigned or chosen at a machine.",
      "The machine it is assigned to or running on keeps the copy it got and finishes with it.",
      "Runs that already ended keep their history.",
    ]) {
      expect(within(dialog).getByText(sentence)).toBeInTheDocument();
    }

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete sequence" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.",
    );
  });

  it("deletes a sequence and drops it from the list without reading the list again", async () => {
    const { requests } = serve(administrator, {
      "GET /api/sequences": () => json([sequence({})]),
      [`DELETE /api/sequences/${installId}`]: () => new Response(null, { status: 204 }),
    });

    fireEvent.click(await screen.findByRole("button", { name: "Delete Install Windows" }));
    const dialog = await screen.findByRole("dialog");
    const readsBefore = requests.filter((request) => request.path === "/api/sequences").length;
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete sequence" }));

    expect(await screen.findByText("No task sequences yet")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === "DELETE")).toBe(true);
    expect(requests.filter((request) => request.path === "/api/sequences")).toHaveLength(
      readsBefore,
    );
  });
});
