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
} from "@tanstack/react-router";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { ImageSummary } from "@/images/images";
import { press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { flowDefinition, flowPhases, flowProblems, windowsImageId } from "@/test/flowSequence";
import { Toasts } from "@/ui/Toast";
import { toasts } from "@/ui/toasts";

import type { AccountView } from "./builder/builderData";
import { SequenceEditorPage } from "./SequenceEditorPage";
import {
  SEQUENCE_VERSION,
  type IfStep,
  type RunScriptStep,
  type SaveSequenceRequest,
  type SequenceProblem,
  type SequenceStep,
  type SequenceView,
} from "./sequences";
import { sequenceSearch } from "./sequenceSearch";
import { newStep } from "./steps";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  mustChangePassword: false,
  roles: ["Administrator"],
};

const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

const sequenceId = "0193a4b2-0000-7000-8000-0000000000e1";

const image: ImageSummary = {
  id: windowsImageId,
  name: "Windows 11 Pro",
  kind: "Wim",
  sha256: "00",
  sizeBytes: 1,
  wimIndex: 1,
  edition: "Professional",
  architecture: "x64",
  version: "10.0.26100.1",
  language: "en-US",
  installedBytes: 1,
  originalFileName: "install.wim",
  uploadedUtc: "2026-09-15T10:00:00Z",
  uploadedBy: "admin",
  bootCapability: null,
  bootDetail: null,
  sourceSha256: null,
};

const account: AccountView = {
  id: "0193a4b2-0000-7000-8000-0000000000c1",
  name: "Deploy",
  userName: "CORP\\ddt-deploy",
  domain: "corp.example",
  hosts: ["fs01.corp.example"],
  runAs: true,
  password: { isSet: true, unreadable: false, updatedUtc: "2026-09-20T10:00:00Z" },
  usedBy: [],
  revision: 1,
  updatedUtc: "2026-09-20T10:00:00Z",
  updatedBy: "admin",
};

// A flat sequence of version 1: partition, apply, a script with a condition of version 1.
const flatSteps: SequenceStep[] = [
  newStep("partition", "p"),
  { ...newStep("applyImage", "i"), imageId: windowsImageId } as SequenceStep,
  {
    ...(newStep("runScript", "s") as RunScriptStep),
    name: "Set wallpaper",
    script: "exit 0",
    conditions: [{ variable: "Model", operator: "Equals", value: "Latitude 7440" }],
  },
];

function view(overrides: Partial<SequenceView> = {}): SequenceView {
  return {
    id: sequenceId,
    name: "Lab PCs",
    description: null,
    revision: 3,
    definition: { version: 1, steps: flatSteps },
    stepPhases: ["WindowsPE", "WindowsPE", "WindowsPE"],
    problems: [],
    warnings: [],
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

// The design's flow: an IF, a group, a repeat, variables and an Account input.
const treeView = (overrides: Partial<SequenceView> = {}) =>
  view({
    definition: flowDefinition,
    stepPhases: [],
    nodePhases: flowPhases,
    ...overrides,
  });

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

type SaveAnswer = (request: SaveSequenceRequest) => Response;

// The server holds one sequence. Saves answer as the given function says; by default they are stored with the
// next revision, as the server does. Facts, rules and machine roles are not served, as by a server before them.
function serve(
  user: CurrentUser,
  initial: SequenceView,
  {
    answer,
    step,
    narrow = false,
    accounts = [account],
  }: { answer?: SaveAnswer; step?: string; narrow?: boolean; accounts?: AccountView[] } = {},
) {
  let stored = initial;
  let deleted = false;
  const saves: SaveSequenceRequest[] = [];
  const reads: string[] = [];

  const store: SaveAnswer = (request) => {
    stored = {
      ...stored,
      name: request.name,
      description: request.description,
      definition: request.definition,
      revision: stored.revision + 1,
      updatedBy: user.userName,
    };
    return json(stored);
  };

  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: narrow && query.includes("max-width"),
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    })),
  );
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = (input instanceof Request ? input.url : input.toString()).replace(
        "http://localhost",
        "",
      );
      const method = init?.method ?? "GET";

      if (method === "GET") {
        reads.push(path);
      }

      switch (`${method} ${path}`) {
        case "GET /api/auth/me":
          return Promise.resolve(json(user));
        case "GET /api/auth/session":
          return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
        case `GET /api/sequences/${sequenceId}`:
          return Promise.resolve(deleted ? new Response(null, { status: 404 }) : json(stored));
        case `PUT /api/sequences/${sequenceId}`: {
          const body = typeof init?.body === "string" ? init.body : "null";
          const request = JSON.parse(body) as SaveSequenceRequest;
          saves.push(request);

          return Promise.resolve((answer ?? store)(request));
        }
        case "GET /api/images":
          return Promise.resolve(json([image]));
        case "GET /api/packages":
          return Promise.resolve(json([]));
        case "GET /api/accounts":
          return Promise.resolve(json(accounts));
        case "GET /api/deployments/options":
          return Promise.resolve(
            json({
              domainConfigured: true,
              requireWebApproval: false,
              zeroTouchEnabled: false,
              serverUtc: "2026-09-16T10:00:00Z",
            }),
          );
        default:
          return Promise.resolve(new Response(null, { status: 404 }));
      }
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
          component: () => <p>All the sequences</p>,
        }),
        createRoute({
          getParentRoute: () => shellRoute,
          path: "/deployment/sequences/$sequenceId",
          validateSearch: sequenceSearch,
          component: SequenceEditorPage,
        }),
      ]),
    ]),
    history: createMemoryHistory({
      initialEntries: [
        `/deployment/sequences/${sequenceId}${step === undefined ? "" : `?step=${step}`}`,
      ],
    }),
  });

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <main>
          <RouterProvider router={router} />
        </main>
        <Toasts />
      </QueryClientProvider>
    </I18nProvider>,
  );

  const remove = () => {
    deleted = true;
  };

  return { saves, reads, queryClient, router, remove };
}

// The debounce of typing is 700 ms.
const saveWait = { timeout: 3_000 };

async function opened(name = "Lab PCs") {
  return screen.findByRole("heading", { level: 1, name });
}

// A node of the flow, by the start of what a screen reader hears for it.
function node(label: string | RegExp): HTMLElement {
  const flow = screen.getByRole("group", { name: /^Flow of / });

  return within(flow).getByRole("button", {
    name: typeof label === "string" ? new RegExp(`^${escape(label)}`) : label,
  });
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// The element that holds a field of the node shown, by the name the server's findings give it.
function field(name: string): HTMLElement {
  const slot = document.querySelector<HTMLElement>(`[data-field="${name}"]`);

  if (slot === null) {
    throw new Error(`No ${name} field shows.`);
  }

  return slot;
}

function tab(name: string) {
  fireEvent.click(screen.getByRole("tab", { name: new RegExp(`^${name}`) }));
}

function ids(steps: readonly SequenceStep[]): unknown[] {
  return steps.map((step) =>
    step.kind === "if"
      ? { [step.id]: { then: ids(step.then), else: ids(step.else) } }
      : step.kind === "group" || step.kind === "repeat"
        ? { [step.id]: ids(step.steps) }
        : step.id,
  );
}

function key(target: Element, name: string, modifiers: Record<string, boolean> = {}) {
  fireEvent.keyDown(target, { key: name, ...modifiers });
  fireEvent.keyUp(target, { key: name, ...modifiers });
}

describe("SequenceEditorPage", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("draws the flow with every node, marks the findings and says where each node is", async () => {
    serve(administrator, treeView({ problems: flowProblems }), { step: "if1" });

    await opened();
    expect(screen.getByText("1 problem")).toBeInTheDocument();
    expect(node("Step 2, If: Is it a Latitude?")).toHaveAttribute("aria-current", "true");
    expect(
      node("Step 1 of Then of 'If: Is it a Latitude?', Apply Windows 11 for Latitudes"),
    ).toHaveAccessibleName(
      "Step 1 of Then of 'If: Is it a Latitude?', Apply Windows 11 for Latitudes, Apply image",
    );
    expect(node(/^Step 1 of 'Group: Berlin office'/)).toHaveAccessibleName(
      "Step 1 of 'Group: Berlin office', Map the site share, Run script, 1 problem",
    );
    // The IF's inspector shows its test.
    expect(screen.getByRole("group", { name: "Go along Then when" })).toBeInTheDocument();
    expect(
      screen.getByText("Machines where Model contains Latitude go along Then."),
    ).toBeInTheDocument();
    // One stop of the Tab key among the nodes and the gaps: the chosen node.
    expect(
      [...document.querySelectorAll<HTMLElement>("[data-flow-node], [aria-haspopup=menu]")].filter(
        (element) => element.tabIndex === 0 && element.closest("[role=group]") !== null,
      ),
    ).toEqual([node("Step 2, If: Is it a Latitude?")]);
  });

  it("passes axe", async () => {
    serve(administrator, treeView({ problems: flowProblems }), { step: "s1" });

    await opened();
    await screen.findByRole("group", { name: "Run only when" });
    await expectNoAxeViolations();
  });

  it("adds a step at a wire with the mouse, and keeps a flat sequence flat in what it saves", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    press(
      screen.getByRole("button", { name: "Add a step between Partition the disk and Apply image" }),
    );
    const menu = await screen.findByRole("menu");
    press(within(menu).getByRole("menuitem", { name: "Run script" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "runScript",
        "applyImage",
        "runScript",
      ]);
    }, saveWait);

    const saved = saves.at(-1);
    // The page sends the highest version it knows; the server stores the lowest the steps need.
    expect(saved?.definition.version).toBe(SEQUENCE_VERSION);
    expect(Object.keys(saved?.definition ?? {})).toEqual(["version", "steps"]);

    for (const step of saved?.definition.steps ?? []) {
      expect(Object.keys(step)).not.toContain("when");
      expect(Object.keys(step)).not.toContain("shares");
      expect(Object.keys(step)).not.toContain("runAs");
    }

    // The new step is chosen, with the focus on it.
    await waitFor(() => {
      expect(node("Step 2, Run script")).toHaveFocus();
    });
    expect(node("Step 2, Run script")).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Run script");
  });

  it("adds a step from the palette by dragging it with the keyboard", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    const palette = screen.getByRole("complementary", { name: "Add to the flow" });
    const pause = within(palette).getByRole("button", { name: /^Pause/ });

    act(() => {
      pause.focus();
    });
    key(pause, "Enter");

    // The drag starts on the next frame, at a gap.
    await waitFor(() => {
      expect(document.activeElement?.getAttribute("aria-label")).toMatch(/^Add a step/);
    });

    for (
      let tries = 0;
      tries < 10 &&
      document.activeElement?.getAttribute("aria-label") !== "Add a step after Set wallpaper";
      tries++
    ) {
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "Tab" });
    }

    expect(document.activeElement).toHaveAccessibleName("Add a step after Set wallpaper");
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Enter" });
    fireEvent.keyUp(document.activeElement ?? document.body, { key: "Enter" });

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "applyImage",
        "runScript",
        "pause",
      ]);
    }, saveWait);
    expect(node("Step 4, Pause")).toHaveAttribute("aria-current", "true");
  });

  it("moves through the flow with the arrow keys, opens a node's fields with Enter and goes back with Escape", async () => {
    serve(administrator, treeView(), { step: "p" });

    await opened();
    const first = node("Step 1, Partition the disk");

    act(() => {
      first.focus();
    });
    fireEvent.keyDown(first, { key: "ArrowDown" });
    expect(node("Step 2, If: Is it a Latitude?")).toHaveFocus();

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowDown" });
    expect(node(/^Step 1 of Then of/)).toHaveFocus();

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowRight" });
    const otherwise = node(/^Step 1 of Else of/);
    expect(otherwise).toHaveFocus();
    expect(otherwise).toHaveAttribute("aria-current", "true");

    fireEvent.keyDown(otherwise, { key: "Enter" });
    const name = screen.getByRole("textbox", { name: "Name" });
    await waitFor(() => {
      expect(name).toHaveFocus();
    });
    expect(name).toHaveValue("Apply Windows 11");

    fireEvent.keyDown(name, { key: "Escape" });
    await waitFor(() => {
      expect(node(/^Step 1 of Else of/)).toHaveFocus();
    });

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    expect(node("Step 2, If: Is it a Latitude?")).toHaveFocus();
  });

  it("moves a node within its list with Alt and an arrow key, says where it went, and duplicates it with Ctrl+D", async () => {
    const { saves } = serve(administrator, treeView(), { step: "a1" });

    await opened();
    const apply = node(/^Step 1 of Then of/);

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "ArrowDown", altKey: true });

    expect(
      node(/^Step 2 of Then of 'If: Is it a Latitude\?', Apply Windows 11 for Latitudes/),
    ).toHaveFocus();
    expect(
      screen.getByText("Apply Windows 11 for Latitudes moved to position 2 of 2."),
    ).toBeInTheDocument();

    // At the end of its list it stays.
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowDown", altKey: true });
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "d", ctrlKey: true });

    await waitFor(() => {
      const branch = saves.at(-1)?.definition.steps[1] as IfStep | undefined;

      expect(branch?.then.map((step) => step.name)).toEqual([
        "Add the Latitude drivers",
        "Apply Windows 11 for Latitudes",
        "Apply Windows 11 for Latitudes",
      ]);
    }, saveWait);
    await waitFor(() => {
      expect(node(/^Step 3 of Then of/)).toHaveFocus();
    });
  });

  it("wraps a node in an IF from its menu", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    const apply = node("Step 2, Apply image");

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "F10", shiftKey: true });
    const menu = await screen.findByRole("menu", { name: "Actions for Apply image" });
    press(within(menu).getByRole("menuitem", { name: "Wrap in an If" }));

    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual([
        "p",
        { [(saves.at(-1)?.definition.steps[1] as IfStep).id]: { then: ["i"], else: [] } },
        "s",
      ]);
    }, saveWait);
    await waitFor(() => {
      expect(node("Step 2, If: If")).toHaveFocus();
    });
  });

  it("removes a node with Delete, brings it back from the toast, and undoes and redoes with Ctrl+Z and Ctrl+Y", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    const apply = node("Step 2, Apply image");

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "Delete" });

    // A removal is saved at once, and the node after it takes the focus.
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    });
    await waitFor(() => {
      expect(node(/^Step 2, Set wallpaper/)).toHaveFocus();
    });

    const toast = screen.getByRole("alertdialog");
    expect(toast).toHaveTextContent("Removed Apply image.");
    press(within(toast).getByRole("button", { name: "Undo" }));

    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "i", "s"]);
    }, saveWait);

    // In a text field, the keys are the field's own.
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Name" }), { key: "z", ctrlKey: true });
    expect(node(/^Step 2, Apply image/)).toBeInTheDocument();

    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    }, saveWait);

    fireEvent.keyDown(document.body, { key: "y", ctrlKey: true });
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "i", "s"]);
    }, saveWait);

    // The header's keys do the same.
    press(screen.getByRole("button", { name: "Undo" }));
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    }, saveWait);
  });

  it("copies and pastes a node with new ids, through the clipboard and without it", async () => {
    let clip = "";
    const writeText = vi.fn((text: string) => {
      clip = text;
      return Promise.resolve();
    });
    const readText = vi.fn(() => Promise.resolve(clip));

    Object.defineProperty(window.navigator, "clipboard", {
      configurable: true,
      value: { writeText, readText },
    });

    try {
      const { saves } = serve(administrator, view(), { step: "s" });

      await opened();
      const script = node(/^Step 3, Set wallpaper/);

      act(() => {
        script.focus();
      });
      fireEvent.keyDown(script, { key: "c", ctrlKey: true });
      expect(writeText).toHaveBeenCalledOnce();
      expect(JSON.parse(clip)).toMatchObject({
        ddtFlow: 1,
        nodes: [{ id: "s", name: "Set wallpaper" }],
      });

      const first = node("Step 1, Partition the disk");
      act(() => {
        first.focus();
      });
      fireEvent.keyDown(first, { key: "v", ctrlKey: true });

      await waitFor(() => {
        expect(saves.at(-1)?.definition.steps).toHaveLength(4);
      }, saveWait);
      const pasted = saves.at(-1)?.definition.steps[1];
      expect(pasted).toMatchObject({ kind: "runScript", name: "Set wallpaper", script: "exit 0" });
      expect(pasted?.id).not.toBe("s");
      await waitFor(() => {
        expect(node(/^Step 2, Set wallpaper/)).toHaveFocus();
      });

      // A browser that keeps the clipboard from the page pastes what the page copied.
      readText.mockRejectedValue(new Error("Not allowed"));
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "v", ctrlKey: true });

      await waitFor(() => {
        expect(saves.at(-1)?.definition.steps).toHaveLength(5);
      }, saveWait);
      expect(new Set(saves.at(-1)?.definition.steps.map((step) => step.id)).size).toBe(5);
    } finally {
      Reflect.deleteProperty(window.navigator, "clipboard");
    }
  });

  it("takes the focus from a finding to a condition's value deep in the flow, and to a variable's field", async () => {
    const problems: SequenceProblem[] = [
      { stepId: "s2", field: "when.parts[0].value", message: "Choose one of the kinds." },
      { stepId: null, field: "variables[1].default", message: "The default is too long." },
    ];

    serve(administrator, treeView({ problems }), { step: "p" });

    await opened();
    tab("Problems");
    press(
      screen.getByRole("button", {
        name: "Choose one of the kinds. Go to Install the site printer.",
      }),
    );

    await waitFor(() => {
      expect(within(field("when.parts[0].value")).getByRole("button")).toHaveFocus();
    });
    expect(node(/^Step 2 of 'Group: Berlin office'/)).toHaveAttribute("aria-current", "true");
    expect(within(field("when.parts[0].value")).getByRole("button")).toHaveAccessibleDescription(
      /Choose one of the kinds\./,
    );

    tab("Problems");
    press(screen.getByRole("button", { name: "The default is too long. Go to its field." }));
    await waitFor(() => {
      expect(screen.getByRole("combobox", { name: "Default" })).toHaveFocus();
    });
    expect(screen.getByRole("combobox", { name: "Default" })).toHaveValue("Standard");
  });

  it("shows a step's conditions of versions 1 and 2, and makes them its when at the first change", async () => {
    const { saves } = serve(administrator, view(), { step: "s" });

    await opened();
    const value = screen.getByRole("textbox", { name: "Value of condition 1" });
    expect(value).toHaveValue("Latitude 7440");
    expect(screen.getByText("Runs only where Model equals Latitude 7440.")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "Latitude 7450" } });

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        conditions: [],
        when: {
          kind: "all",
          parts: [{ kind: "test", variable: "Model", operator: "Equals", value: "Latitude 7450" }],
        },
      });
    }, saveWait);
  });

  it("completes a name in a template and fills it in for a sample machine", async () => {
    const { saves } = serve(administrator, treeView(), { step: "sv" });

    await opened();
    const value = screen.getByRole("combobox", { name: "Value" });
    expect(value).toHaveValue("PC-{{SerialNumber|alnum|right:8}}");
    expect(screen.getByText("PC-PF4K2Z7Q")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "WS-{{Seri" } });
    const list = screen.getByRole("listbox", { name: "Values to use" });
    expect(within(list).getAllByRole("option")[0]).toHaveTextContent("SerialNumber");
    fireEvent.keyDown(value, { key: "Enter" });

    expect(value).toHaveValue("WS-{{SerialNumber}}");
    expect(screen.queryByRole("listbox", { name: "Values to use" })).not.toBeInTheDocument();
    expect(screen.getByText("WS-PF4K2Z7Q")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "WS-{{Sreial}}" } });
    expect(
      screen.getByText(
        "{{Sreial}} uses Sreial, which is not a machine fact or a declared value. Check the spelling.",
      ),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({ value: "WS-{{Sreial}}" });
    }, saveWait);
  });

  it("runs a Windows script as a stored account, chosen from the server's accounts", async () => {
    const { saves } = serve(administrator, treeView(), { step: "s3" });

    await opened();
    const runAs = field("runAs");
    await waitFor(() => {
      expect(within(runAs).getByRole("button")).toHaveTextContent("SYSTEM");
    });
    press(within(runAs).getByRole("button"));
    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual([
      "SYSTEM",
      "Deploy (CORP\\ddt-deploy)corp.example",
    ]);
    press(options[1] ?? document.body);

    await waitFor(() => {
      const repeat = saves.at(-1)?.definition.steps[6];

      expect(repeat?.kind === "repeat" ? repeat.steps[0] : null).toMatchObject({
        runAs: { accountId: account.id, input: null },
      });
    }, saveWait);
  });

  it("renames a variable everywhere it is used", async () => {
    const { saves } = serve(administrator, treeView(), { step: "p" });

    await opened();
    tab("Variables");
    const row = screen.getByRole("button", { name: /^ComputerName/ });
    expect(row).toHaveTextContent("Used by 2 nodes");
    press(row);

    fireEvent.change(screen.getByRole("textbox", { name: "Name" }), {
      target: { value: "PcName" },
    });
    press(screen.getByRole("button", { name: "Rename everywhere" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.variables?.[0]?.name).toBe("PcName");
    }, saveWait);
    const steps = saves.at(-1)?.definition.steps ?? [];
    expect(steps[2]).toMatchObject({ variable: "PcName" });
    expect(steps[7]).toMatchObject({ message: "Check the asset tag of {{PcName}}." });
  });

  it("shows a viewer the flow without letting anything change", async () => {
    const { saves } = serve(viewer, treeView(), { step: "s1" });

    expect(
      await screen.findByText(
        "Only administrators change task sequences. You can look at this one.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("complementary", { name: "Add to the flow" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Add a step/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Remove/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Wrap/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add a condition" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add a share" })).not.toBeInTheDocument();
    expect(screen.queryByText("All changes saved")).not.toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveAttribute("readonly");
    expect(screen.getByRole("textbox", { name: "Script" })).toHaveAttribute("readonly");
    // A choice reads as text.
    expect(screen.getByRole("textbox", { name: "Interpreter" })).toHaveValue("PowerShell");

    const share = node(/^Step 1 of 'Group: Berlin office'/);
    act(() => {
      share.focus();
    });
    fireEvent.keyDown(share, { key: "Delete" });
    fireEvent.keyDown(share, { key: "ArrowDown", altKey: true });
    fireEvent.keyDown(share, { key: "x", ctrlKey: true });
    expect(node(/^Step 1 of 'Group: Berlin office'/)).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("shows the outline on a phone, and a node's fields in a drawer", async () => {
    serve(administrator, treeView(), { narrow: true });

    await opened();
    expect(screen.queryByRole("group", { name: /^Flow of / })).not.toBeInTheDocument();
    const outline = screen.getByRole("treegrid", { name: "Outline of Lab PCs" });
    const row = within(outline).getByRole("row", { name: /^2 Step 2, If: Is it a Latitude\?/ });
    expect(within(outline).getByRole("row", { name: /^2\.1 Then/ })).toBeInTheDocument();
    expect(
      within(outline).getByRole("row", { name: /^2\.1\.1 Step 1 of Then of/ }),
    ).toBeInTheDocument();

    press(row);

    const drawer = await screen.findByRole("dialog", { name: "If: Is it a Latitude?" });
    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveValue("Is it a Latitude?");
  });

  it("names who saved in between, and keeps this page's version only once confirmed", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    let conflicted = false;
    const { saves } = serve(administrator, view(), {
      answer: (request) => {
        if (!conflicted) {
          conflicted = true;
          return json(theirs, 409);
        }

        return json({ ...theirs, name: request.name, revision: 5, updatedBy: "admin" });
      },
    });

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs (mine)" },
    });

    const title = await screen.findByText(
      /^bob saved this sequence at .* while you were editing\.$/,
      undefined,
      saveWait,
    );
    const notice = title.closest<HTMLElement>("[role=status]");

    if (notice === null) {
      throw new Error("The conflict is not in its notice.");
    }

    expect(notice).toHaveTextContent("They changed the name.");
    fireEvent.click(within(notice).getByRole("button", { name: "Keep mine" }));
    const dialog = await screen.findByRole("dialog", { name: "Save your version over theirs?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save my version" }));

    await waitFor(() => {
      expect(saves).toHaveLength(2);
    });
    expect(saves[1]).toMatchObject({ revision: 4, name: "Lab PCs (mine)" });
    expect(await screen.findByText(/^All changes saved at /)).toBeInTheDocument();
  });

  it("takes the other version and drops this page's edits, without reading it again", async () => {
    const theirs = view({ revision: 4, name: "Lab PCs (bob)", updatedBy: "bob" });
    const { saves, reads } = serve(administrator, view(), { answer: () => json(theirs, 409) });

    await opened();
    await waitFor(() => {
      expect(screen.getByText("All changes saved")).toBeInTheDocument();
    });
    const readsBefore = reads.length;
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab PCs (mine)" },
    });

    const takeTheirs = await screen.findByRole("button", { name: "Use theirs" }, saveWait);
    await waitFor(() => {
      expect(takeTheirs).toBeEnabled();
    });
    fireEvent.click(takeTheirs);

    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveValue("Lab PCs (bob)");
    expect(saves).toHaveLength(1);
    // Their copy came with the refusal.
    expect(reads.slice(readsBefore)).not.toContain(`/api/sequences/${sequenceId}`);
  });

  it("forgets what to undo once it shows another administrator's save", async () => {
    const { saves, queryClient } = serve(administrator, view());

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Description" }), {
      target: { value: "For room 4" },
    });
    await waitFor(() => {
      expect(saves).toHaveLength(1);
    }, saveWait);
    await screen.findByText(/^All changes saved at /);

    act(() => {
      queryClient.setQueryData(
        ["sequence", sequenceId],
        view({ revision: 9, name: "Lab PCs, room 4", description: "Theirs", updatedBy: "bob" }),
      );
    });
    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Description" })).toHaveValue("Theirs");
    });
    expect(screen.getByText(/^bob saved it at /)).toBeInTheDocument();

    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });
    expect(screen.getByRole("textbox", { name: "Description" })).toHaveValue("Theirs");
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(1);
  });

  it("shows the server's refusal of a name at the field", async () => {
    serve(administrator, view(), {
      answer: () =>
        json(
          { title: "Invalid", errors: { name: ["Another sequence is already called Lab."] } },
          400,
        ),
    });

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });

    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveAccessibleDescription(
        "Another sequence is already called Lab.",
      );
    }, saveWait);
    expect(
      screen.getByText("Not saved: Another sequence is already called Lab."),
    ).toBeInTheDocument();
  });

  it("saves at once on leaving, and goes once it is saved", async () => {
    const { saves } = serve(administrator, view());

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });
    fireEvent.click(screen.getByRole("link", { name: "Task sequences" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
    expect(saves).toEqual([expect.objectContaining({ name: "Lab", revision: 3 })]);
  });

  it("asks before leaving when the changes cannot be saved", async () => {
    serve(administrator, view(), {
      answer: () => json(view({ revision: 4, updatedBy: "bob" }), 409),
    });

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });
    fireEvent.click(screen.getByRole("link", { name: "Task sequences" }));

    const dialog = await screen.findByRole("dialog", { name: "Leave without saving?" });
    expect(dialog).toHaveTextContent(
      "Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.",
    );

    fireEvent.click(within(dialog).getByRole("button", { name: "Leave without saving" }));

    expect(await screen.findByText("All the sequences")).toBeInTheDocument();
  });

  it("stops saving, unsaved edits too, once someone else deleted the sequence", async () => {
    const { saves, queryClient, remove } = serve(administrator, view());

    await opened();
    tab("Sequence");
    fireEvent.change(screen.getByRole("textbox", { name: "Sequence name" }), {
      target: { value: "Lab" },
    });

    // As the live connection does for a sequenceChanged without a revision.
    remove();
    act(() => {
      void queryClient.invalidateQueries({ queryKey: ["sequence", sequenceId] });
    });

    expect(await screen.findByText("This sequence was deleted")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Sequence name" })).toHaveAttribute("readonly");

    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("says when the sequence does not exist", async () => {
    const { router } = serve(administrator, view());

    await opened();

    await act(() =>
      router.navigate({
        to: "/deployment/sequences/$sequenceId",
        params: { sequenceId: "0193a4b2-0000-7000-8000-0000000000ff" },
      }),
    );

    expect(await screen.findByText("Sequence not found")).toBeInTheDocument();
  });
});
