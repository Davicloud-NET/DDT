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
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { HardwareModelCount } from "@/machines/machines";
import type { PackageSummary, UpdatePackageRequest } from "@/packages/packages";
import type {
  RunScriptStep,
  SequenceStep,
  SequenceSummary,
  SequenceView,
} from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";

import { PackagesPage } from "./PackagesPage";

const administrator: CurrentUser = {
  id: "u",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

const viewer: CurrentUser = { ...administrator, userName: "viewer", roles: ["Viewer"] };

function item(overrides: Partial<PackageSummary>): PackageSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000b1",
    name: "Latitude drivers",
    kind: "Drivers",
    sha256: "00",
    sizeBytes: 300 * 1024 ** 2,
    expandedBytes: 900 * 1024 ** 2,
    fileCount: 14,
    targets: [{ manufacturer: "Dell Inc.", model: "Latitude*" }],
    description: null,
    originalFileName: "latitude.zip",
    uploadedUtc: new Date(Date.now() - 3_600_000).toISOString(),
    uploadedBy: "admin",
    ...overrides,
  };
}

const drivers = item({});
const scripts = item({
  id: "0193a4b2-0000-7000-8000-0000000000b2",
  name: "Lab scripts",
  kind: "Files",
  targets: [],
  fileCount: 2,
  originalFileName: "scripts.zip",
});

const models: HardwareModelCount[] = [
  { manufacturer: "Dell Inc.", model: "Latitude 7440", machines: 3 },
  { manufacturer: "Dell Inc.", model: "Latitude 5440", machines: 2 },
  { manufacturer: "Microsoft Corporation", model: "Virtual Machine", machines: 4 },
];

function summary(id: string, name: string): SequenceSummary {
  return {
    id,
    name,
    description: null,
    revision: 1,
    stepCount: 1,
    problemCount: 0,
    warningCount: 0,
    erasesDisk: false,
    needsComputerName: false,
    continuesInWindows: false,
    updatedUtc: "2026-09-15T10:00:00Z",
    updatedBy: "admin",
  };
}

function view(id: string, name: string, steps: SequenceStep[]): SequenceView {
  return {
    ...summary(id, name),
    definition: { version: 1, steps },
    stepPhases: steps.map(() => "WindowsPE"),
    problems: [],
    warnings: [],
  };
}

const install = view("s1", "Install Windows", [newStep("injectDrivers", "d")]);
const lab = view("s2", "Lab setup", [
  { ...(newStep("runScript", "r") as RunScriptStep), packageId: scripts.id },
]);

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
  packages: () => PackageSummary[],
  handlers: Record<string, Handler> = {},
) {
  const requests: Sent[] = [];
  const defaults: Record<string, Handler> = {
    "GET /api/packages": () => json(packages()),
    "GET /api/machines/models": () => json(models),
    "GET /api/sequences": () => json([summary("s1", install.name), summary("s2", lab.name)]),
    "GET /api/sequences/s1": () => json(install),
    "GET /api/sequences/s2": () => json(lab),
    "GET /api/images/uploads": () => json([]),
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
      createRoute({ getParentRoute: () => rootRoute, path: "/packages", component: PackagesPage }),
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/sequences/$sequenceId",
        component: () => <p>A sequence</p>,
      }),
    ]),
    history: createMemoryHistory({ initialEntries: ["/packages"] }),
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

function row(name: string): HTMLElement {
  const found = screen.getByRole("button", { name: `Delete ${name}` }).closest("tr");

  if (found === null) {
    throw new Error(`${name} is not in a table row.`);
  }

  return found;
}

describe("PackagesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("explains what packages are for when there is none", async () => {
    serve(administrator, () => []);

    expect(await screen.findByRole("heading", { name: "No packages yet" })).toBeInTheDocument();
    expect(
      screen.getByText(/Upload a zip holding the driver folder of one hardware model/),
    ).toBeInTheDocument();
  });

  it("uploads a zip as the kind chosen and names the package it became", async () => {
    let list: PackageSummary[] = [];
    const session = {
      id: "0193a4b2-0000-7000-8000-0000000000u9",
      fileName: "scripts.zip",
      length: 4,
      lastModified: 1_000,
      offset: 0,
      chunkBytes: 8,
      kind: "Files",
    };

    const requests = serve(administrator, () => list, {
      "POST /api/images/uploads": () => json(session, 201),
      [`PATCH /api/images/uploads/${session.id}`]: () =>
        new Response(null, { status: 204, headers: { "Upload-Offset": "4" } }),
      [`POST /api/images/uploads/${session.id}/complete`]: () => {
        list = [scripts];
        return json(scripts, 201);
      },
    });

    fireEvent.change(await screen.findByLabelText("Kind of package"), {
      target: { value: "Files" },
    });
    fireEvent.change(screen.getByLabelText("Zip file"), {
      target: { files: [new File(["zip!"], "scripts.zip", { lastModified: 1_000 })] },
    });

    expect(await screen.findByText("Added Lab scripts from scripts.zip.")).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Delete Lab scripts" })).toBeInTheDocument();
    expect(
      requests.find(
        (request) => request.method === "POST" && request.path === "/api/images/uploads",
      )?.body,
    ).toEqual({ fileName: "scripts.zip", length: 4, lastModified: 1_000, kind: "Files" });
  });

  it("shows what each package holds, the machines it matches and the sequences that use it", async () => {
    serve(administrator, () => [drivers, scripts]);

    await screen.findByRole("button", { name: "Delete Latitude drivers" });
    const driverRow = row("Latitude drivers");
    const scriptRow = row("Lab scripts");

    expect(driverRow).toHaveTextContent("14 files");
    expect(within(driverRow).getByText("5 machines")).toBeInTheDocument();
    expect(
      await within(driverRow).findByRole("link", { name: "Install Windows" }),
    ).toBeInTheDocument();
    expect(within(driverRow).queryByRole("link", { name: "Lab setup" })).toBeNull();

    expect(within(scriptRow).getByRole("link", { name: "Lab setup" })).toBeInTheDocument();
    expect(scriptRow).toHaveTextContent(
      "Chosen by the Run script steps that name it, not by model.",
    );
  });

  it("warns about a driver package that targets no model", async () => {
    serve(administrator, () => [item({ targets: [] })]);

    expect(
      await screen.findByText("Targets no model, so no machine gets these drivers."),
    ).toBeInTheDocument();
    expect(row("Latitude drivers")).toHaveTextContent("No registered machine");
  });

  it("saves a changed target in place", async () => {
    const saves: UpdatePackageRequest[] = [];

    serve(administrator, () => [drivers], {
      [`PUT /api/packages/${drivers.id}`]: (request) => {
        const body = request.body as UpdatePackageRequest;
        saves.push(body);
        return json({ ...drivers, targets: body.targets });
      },
    });

    fireEvent.change(await screen.findByLabelText("Model of target 1 of Latitude drivers"), {
      target: { value: "Latitude 7440" },
    });

    await waitFor(
      () => {
        expect(saves).toEqual([
          {
            name: "Latitude drivers",
            description: null,
            targets: [{ manufacturer: "Dell Inc.", model: "Latitude 7440" }],
          },
        ]);
      },
      { timeout: 3_000 },
    );
    expect(await within(row("Latitude drivers")).findByText("Saved")).toBeInTheDocument();
    expect(row("Latitude drivers")).toHaveTextContent("3 machines");
  });

  it("shows the server's refusal of the targets at them", async () => {
    serve(administrator, () => [drivers], {
      [`PUT /api/packages/${drivers.id}`]: () =>
        json(
          { title: "Invalid", errors: { targets: ["Put at least 3 characters before *."] } },
          400,
        ),
    });

    fireEvent.change(await screen.findByLabelText("Model of target 1 of Latitude drivers"), {
      target: { value: "L*" },
    });

    await waitFor(
      () => {
        expect(
          screen.getByLabelText("Model of target 1 of Latitude drivers"),
        ).toHaveAccessibleDescription("Put at least 3 characters before *.");
      },
      { timeout: 3_000 },
    );
  });

  it("names the sequences a deletion affects and shows the server's refusal in place", async () => {
    serve(administrator, () => [drivers, scripts], {
      [`DELETE /api/packages/${scripts.id}`]: () =>
        json(
          {
            title:
              "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.",
          },
          409,
        ),
    });

    await within(await screen.findByRole("table")).findByRole("link", { name: "Lab setup" });
    fireEvent.click(screen.getByRole("button", { name: "Delete Lab scripts" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Lab scripts?" });
    expect(dialog).toHaveTextContent(
      "Lab scripts (300 MB) is deleted from the library. The sequence Lab setup names it in a Run script step and shows a problem until another package is chosen. The server refuses while a machine is assigned or runs a sequence that uses it.",
    );

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete package" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Machines are waiting to install this package or are installing it.",
    );
  });

  it("does not claim no sequence names a package while a sequence could not be read", async () => {
    serve(administrator, () => [drivers, scripts], {
      "GET /api/sequences/s1": () => json({ title: "The server failed." }, 500),
    });

    fireEvent.click(await screen.findByRole("button", { name: "Delete Lab scripts" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete Lab scripts?" });
    expect(dialog).toHaveTextContent(
      "Lab scripts (300 MB) is deleted from the library. Which sequences name it is not known, because not every sequence could be read; those that do show a problem until another package is chosen.",
    );
    expect(dialog).not.toHaveTextContent("No sequence names it.");
  });

  it("shows a viewer the library without changes", async () => {
    serve(viewer, () => [drivers]);

    expect(await screen.findByLabelText("Name of Latitude drivers")).toBeDisabled();
    expect(screen.getByLabelText("Model of target 1 of Latitude drivers")).toBeDisabled();
    expect(screen.queryByLabelText("Zip file")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete Latitude drivers" })).toBeNull();
  });
});
