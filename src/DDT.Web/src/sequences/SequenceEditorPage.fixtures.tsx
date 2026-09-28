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
import { fireEvent, render, screen, within } from "@testing-library/react";
import { vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { ImageSummary } from "@/images/images";
import { flowDefinition, flowPhases, windowsImageId } from "@/test/flowSequence";
import { ToastRegion } from "@/ui/ToastRegion";

import type { AccountView } from "@/accounts/accounts";
import { SequenceEditorPage } from "./SequenceEditorPage";
import type { RunScriptStep, SaveSequenceRequest, SequenceStep, SequenceView } from "./sequences";
import { sequenceSearch } from "./sequenceSearch";
import { newStep } from "./steps";

export const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  mustChangePassword: false,
  roles: ["Administrator"],
};

export const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

export const sequenceId = "0193a4b2-0000-7000-8000-0000000000e1";

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

export const account: AccountView = {
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

export function view(overrides: Partial<SequenceView> = {}): SequenceView {
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
export const treeView = (overrides: Partial<SequenceView> = {}) =>
  view({
    definition: flowDefinition,
    stepPhases: [],
    nodePhases: flowPhases,
    ...overrides,
  });

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

type SaveAnswer = (request: SaveSequenceRequest) => Response;

interface ServeOptions {
  answer?: SaveAnswer;
  step?: string;
  narrow?: boolean;
  accounts?: AccountView[];
}

// jsdom has no scrollTo or matchMedia; a narrow window matches the phone's max-width queries.
function stubWindow(narrow: boolean) {
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
}

// The server holds one sequence. Saves answer as the given function says; by default they are stored with the
// next revision, as the server does. Facts, rules and machine roles are not served, as by a server before them.
function stubServer(
  user: CurrentUser,
  initial: SequenceView,
  answer: SaveAnswer | undefined,
  accounts: AccountView[],
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

  const remove = () => {
    deleted = true;
  };

  return { saves, reads, remove };
}

function editorRouter(step: string | undefined) {
  const rootRoute = createRootRoute();
  const shellRoute = createRoute({ getParentRoute: () => rootRoute, id: "shell" });

  return createRouter({
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
}

// The editor on a server that holds one sequence, opened at the given step.
export function serve(
  user: CurrentUser,
  initial: SequenceView,
  { answer, step, narrow = false, accounts = [account] }: ServeOptions = {},
) {
  stubWindow(narrow);
  const { saves, reads, remove } = stubServer(user, initial, answer, accounts);

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = editorRouter(step);

  render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <main>
          <RouterProvider router={router} />
        </main>
        <ToastRegion />
      </QueryClientProvider>
    </I18nProvider>,
  );

  return { saves, reads, queryClient, router, remove };
}

// The debounce of typing is 700 ms.
export const saveWait = { timeout: 3_000 };

export async function opened(name = "Lab PCs") {
  return screen.findByRole("heading", { level: 1, name });
}

// A node of the flow, by the start of what a screen reader hears for it.
export function node(label: string | RegExp): HTMLElement {
  const flow = screen.getByRole("group", { name: /^Flow of / });

  return within(flow).getByRole("button", {
    name: typeof label === "string" ? new RegExp(`^${escape(label)}`) : label,
  });
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// The element that holds a field of the node shown, by the name the server's findings give it.
export function field(name: string): HTMLElement {
  const slot = document.querySelector<HTMLElement>(`[data-field="${name}"]`);

  if (slot === null) {
    throw new Error(`No ${name} field shows.`);
  }

  return slot;
}

export function tab(name: string) {
  fireEvent.click(screen.getByRole("tab", { name: new RegExp(`^${name}`) }));
}

// The ids of the steps as nested lists, to compare the tree's shape in one assertion.
export function ids(steps: readonly SequenceStep[]): unknown[] {
  return steps.map((step) =>
    step.kind === "if"
      ? { [step.id]: { then: ids(step.then), else: ids(step.else) } }
      : step.kind === "group" || step.kind === "repeat"
        ? { [step.id]: ids(step.steps) }
        : step.id,
  );
}

export function key(target: Element, name: string, modifiers: Record<string, boolean> = {}) {
  fireEvent.keyDown(target, { key: name, ...modifiers });
  fireEvent.keyUp(target, { key: name, ...modifiers });
}
