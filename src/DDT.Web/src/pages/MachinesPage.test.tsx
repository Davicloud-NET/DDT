import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import type { DeploymentOptionsView, DeploymentSummary } from "@/deployments/deployments";
import type { ImageSummary } from "@/images/images";
import { upsertMachine, type MachineSummary } from "@/machines/machines";

import { MachinesPage } from "./MachinesPage";

const now = new Date("2026-09-16T10:10:00Z");

function secondsBefore(seconds: number): string {
  return new Date(now.getTime() - seconds * 1000).toISOString();
}

function machine(overrides: Partial<MachineSummary>): MachineSummary {
  return {
    id: "0193a4b2-0000-7000-8000-000000000001",
    state: "Pending",
    smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    manufacturer: "Microsoft Corporation",
    model: "Virtual Machine",
    serialNumber: "1234",
    assignedName: null,
    agentVersion: "1.0.0",
    firstSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenAddress: "172.25.132.98",
    signedInBy: null,
    firstSeenAddress: "172.25.132.98",
    everApproved: false,
    disks: "Disk 0: Msft Virtual Disk, 64 GB, SCSI",
    eligibleDiskCount: 1,
    deployment: null,
    ...overrides,
  };
}

function deployment(overrides: Partial<DeploymentSummary>): DeploymentSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000d1",
    imageId: "0193a4b2-0000-7000-8000-0000000000a1",
    imageName: "Windows 11 Pro",
    state: "Assigned",
    step: null,
    percent: 0,
    source: "Web",
    requestedBy: "operator",
    createdUtc: "2026-09-16T10:01:00Z",
    startedUtc: null,
    finishedUtc: null,
    error: null,
    ...overrides,
  };
}

function image(overrides: Partial<ImageSummary>): ImageSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000a1",
    name: "Windows 11 Pro",
    kind: "Wim",
    sha256: "a".repeat(64),
    sizeBytes: 2 * 1024 ** 3,
    wimIndex: 1,
    edition: "Professional",
    architecture: "x64",
    version: "10.0.26100.1",
    language: "en-US",
    installedBytes: 8 * 1024 ** 3,
    originalFileName: "install.wim",
    uploadedUtc: "2026-09-15T10:00:00Z",
    uploadedBy: "admin",
    ...overrides,
  };
}

function options(overrides: Partial<DeploymentOptionsView>): DeploymentOptionsView {
  return {
    domainConfigured: false,
    requireWebApproval: false,
    zeroTouchEnabled: false,
    serverUtc: now.toISOString(),
    ...overrides,
  };
}

const operator: CurrentUser = {
  id: "u",
  userName: "operator",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Operator"],
};

interface Answer {
  status?: number;
  body?: unknown;
}

interface Call {
  method: string;
  path: string;
  body: unknown;
}

// Answers the machine list and the current user, plus the answers given as "METHOD path". Everything else
// gets a 401, as it would without a session.
function renderWith(
  machines: MachineSummary[],
  user: CurrentUser | null = null,
  answers: Record<string, Answer> = {},
) {
  const calls: Call[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const path = url.replace("http://localhost", "");
      const method = init?.method ?? "GET";

      calls.push({
        method,
        path,
        body: typeof init?.body === "string" ? JSON.parse(init.body) : null,
      });

      const answer = answers[`${method} ${path}`];

      if (answer !== undefined) {
        const status = answer.status ?? 200;

        return Promise.resolve(
          new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), { status }),
        );
      }

      const body: unknown = path.endsWith("/api/machines")
        ? machines
        : path.endsWith("/api/auth/me")
          ? user
          : null;

      return Promise.resolve(
        new Response(JSON.stringify(body), { status: body === null ? 401 : 200 }),
      );
    }),
  );

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <MachinesPage />
    </QueryClientProvider>,
  );

  return { calls, queryClient };
}

function row(text: string): HTMLElement {
  const cell = screen.getByText(text);
  const tableRow = cell.closest("tr");

  if (tableRow === null) {
    throw new Error(`${text} is not in a table row.`);
  }

  return tableRow;
}

function cell(text: string): HTMLElement {
  const tableCell = screen.getByText(text).closest("td");

  if (tableCell === null) {
    throw new Error(`${text} is not in a table cell.`);
  }

  return tableCell;
}

describe("MachinesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("explains that no machine has registered yet", async () => {
    renderWith([]);

    expect(screen.getByRole("heading", { level: 1, name: "Machines" })).toBeInTheDocument();
    expect(
      await screen.findByRole("heading", { level: 2, name: "No machines yet" }),
    ).toBeInTheDocument();
  });

  it("lists a registered machine with its state and MAC", async () => {
    renderWith([machine({ signedInBy: "bob", everApproved: true })]);

    expect(await screen.findByText("Virtual Machine")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
    expect(screen.getByText("00:15:5D:01:02:03")).toBeInTheDocument();
    expect(screen.getByText("Signed in by bob")).toBeInTheDocument();
  });

  it("offers operators to remove machines nobody approved, and all of them from one address once", async () => {
    renderWith(
      [
        machine({ id: "1", firstSeenAddress: "10.0.0.9" }),
        machine({ id: "2", firstSeenAddress: "10.0.0.9" }),
        machine({ id: "3", firstSeenAddress: "10.0.0.5" }),
        machine({ id: "4", firstSeenAddress: "10.0.0.9", everApproved: true }),
      ],
      operator,
    );

    expect(await screen.findAllByRole("button", { name: "Remove" })).toHaveLength(3);
    expect(screen.getAllByRole("button", { name: "Remove all 2 from 10.0.0.9" })).toHaveLength(1);
  });

  it("offers to remove a rejected machine, but does not count it with the strays", async () => {
    const { calls } = renderWith(
      [
        machine({ id: "1", firstSeenAddress: "10.0.0.7" }),
        machine({
          id: "2",
          assignedName: "PC-REJECTED",
          state: "Rejected",
          everApproved: true,
          firstSeenAddress: "10.0.0.7",
        }),
      ],
      operator,
      { "DELETE /api/machines/2": { status: 204 } },
    );

    await screen.findByText("PC-REJECTED");

    expect(screen.getAllByRole("button", { name: "Remove" })).toHaveLength(2);
    expect(screen.queryByRole("button", { name: /^Remove all/ })).not.toBeInTheDocument();

    fireEvent.click(within(row("PC-REJECTED")).getByRole("button", { name: "Remove" }));

    await waitFor(() => {
      expect(calls).toContainEqual({ method: "DELETE", path: "/api/machines/2", body: null });
    });
  });

  it("does not offer to remove a waiting machine with an assigned image, nor count it with the strays", async () => {
    renderWith(
      [
        machine({ id: "1", firstSeenAddress: "10.0.0.5" }),
        machine({ id: "2", firstSeenAddress: "10.0.0.5" }),
        machine({
          id: "3",
          assignedName: "PC-ASSIGNED",
          firstSeenAddress: "10.0.0.5",
          deployment: deployment({ state: "Assigned" }),
        }),
      ],
      operator,
    );

    await screen.findByText("PC-ASSIGNED");

    expect(
      within(row("PC-ASSIGNED")).queryByRole("button", { name: /^Remove/ }),
    ).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Remove" })).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Remove all 2 from 10.0.0.5" })).toBeInTheDocument();
  });

  it("shows each machine's deployment: step, percent and time for a running one, the error for a failed one", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    renderWith([
      machine({
        id: "1",
        assignedName: "PC-RUNNING",
        state: "Deploying",
        deployment: deployment({
          state: "Running",
          step: "Download",
          percent: 45,
          startedUtc: secondsBefore(125),
        }),
      }),
      // The check before the start failed, so the disk was never touched and no step is recorded.
      machine({
        id: "2",
        assignedName: "PC-FAILED",
        state: "Failed",
        deployment: deployment({
          state: "Failed",
          error: "The disk is smaller than the image needs.",
          finishedUtc: secondsBefore(500),
        }),
      }),
      machine({
        id: "6",
        assignedName: "PC-STOPPED",
        state: "Failed",
        deployment: deployment({
          state: "Failed",
          step: "Apply",
          percent: 40,
          error: "Stopped by operator.",
          startedUtc: secondsBefore(900),
          finishedUtc: secondsBefore(800),
        }),
      }),
      machine({
        id: "3",
        assignedName: "PC-DONE",
        state: "Done",
        deployment: deployment({
          state: "Done",
          step: "Reboot",
          percent: 100,
          finishedUtc: secondsBefore(300),
        }),
      }),
      machine({
        id: "4",
        assignedName: "PC-CANCELLED",
        state: "Approved",
        deployment: deployment({ state: "Cancelled" }),
      }),
      machine({ id: "5", assignedName: "PC-NONE", state: "Approved" }),
    ]);

    await screen.findByText("PC-RUNNING");

    const running = within(row("PC-RUNNING"));
    expect(running.getByText("Windows 11 Pro")).toBeInTheDocument();
    expect(running.getByText("Download 45%")).toBeInTheDocument();
    expect(running.getByRole("progressbar", { name: "Download progress" })).toHaveAttribute(
      "value",
      "45",
    );
    expect(running.getByText("Running for 2 min 5 s")).toBeInTheDocument();

    const failed = cell("The disk is smaller than the image needs.");
    expect(row("PC-FAILED")).toContainElement(failed);
    expect(within(failed).getByText("Failed")).toBeInTheDocument();
    expect(within(failed).queryByText(/Failed at/)).not.toBeInTheDocument();

    const stopped = within(row("PC-STOPPED"));
    expect(stopped.getByText("Failed at Apply")).toBeInTheDocument();
    expect(stopped.getByText("Stopped by operator.")).toBeInTheDocument();

    expect(within(row("PC-DONE")).getByText(/^Done, /)).toBeInTheDocument();
    expect(within(row("PC-CANCELLED")).getByText("Cancelled")).toBeInTheDocument();
    expect(within(row("PC-NONE")).getByText("None")).toBeInTheDocument();
  });

  it("tells before assigning that a machine waiting at the prompt gets authorized, then assigns", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    const waiting = machine({ lastSeenUtc: secondsBefore(30) });
    const { calls } = renderWith([waiting], operator, {
      "GET /api/images": {
        body: [
          image({}),
          image({
            id: "0193a4b2-0000-7000-8000-0000000000a2",
            name: "Windows 11 Pro ARM",
            architecture: "arm64",
          }),
        ],
      },
      "GET /api/deployments/options": { body: options({}) },
      [`POST /api/machines/${waiting.id}/deployments`]: {
        body: { ...waiting, state: "Approved", deployment: deployment({}) },
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));

    const dialog = await screen.findByRole("dialog", {
      name: "Assign an image to Virtual Machine (00:15:5D:01:02:03)",
    });

    expect(
      await within(dialog).findByText(
        "All data on the disk of Virtual Machine (00:15:5D:01:02:03) will be erased and Windows 11 Pro installed.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "Model: Virtual Machine. Reported disks: Disk 0: Msft Virtual Disk, 64 GB, SCSI.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "This also authorizes the machine, which then receives the image and the deployment passwords.",
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();

    // Only x64 images deploy.
    expect(
      within(dialog)
        .getAllByRole("option")
        .map((option) => option.textContent),
    ).toEqual(["Windows 11 Pro, en-US, 10.0.26100.1"]);

    fireEvent.change(within(dialog).getByLabelText("Computer name"), {
      target: { value: "PC-042" },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Assign image" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(calls).toContainEqual({
      method: "POST",
      path: `/api/machines/${waiting.id}/deployments`,
      body: { imageId: "0193a4b2-0000-7000-8000-0000000000a1", computerName: "PC-042" },
    });
    expect(screen.getByText("Assigned by operator")).toBeInTheDocument();
  });

  it.each([
    {
      waiting: "not seen for a while, with zero touch networks set",
      seconds: 600,
      signedInBy: null,
      settings: options({ zeroTouchEnabled: true }),
      lastSeen: "Last seen from 172.25.132.98 10 minutes ago; nobody has signed in at it.",
      consequence:
        "It stays waiting until someone signs in at it or it netboots from a zero touch network.",
    },
    {
      waiting: "not seen for a while, without zero touch networks",
      seconds: 600,
      signedInBy: null,
      settings: options({}),
      lastSeen: "Last seen from 172.25.132.98 10 minutes ago; nobody has signed in at it.",
      consequence: "It stays waiting until someone signs in at it.",
    },
    {
      waiting: "at the prompt, when the server requires web approval",
      seconds: 30,
      signedInBy: null,
      settings: options({ requireWebApproval: true, zeroTouchEnabled: true }),
      lastSeen: "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
      consequence: "It stays waiting until someone signs in at it.",
    },
  ])(
    "says that a machine $waiting stays waiting after the assignment",
    async ({ seconds, signedInBy, settings, lastSeen, consequence }) => {
      vi.useFakeTimers({ toFake: ["Date"], now });

      renderWith([machine({ lastSeenUtc: secondsBefore(seconds), signedInBy })], operator, {
        "GET /api/images": { body: [image({})] },
        "GET /api/deployments/options": { body: settings },
      });

      fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
      const dialog = await screen.findByRole("dialog");

      expect(within(dialog).getByText(lastSeen)).toBeInTheDocument();
      expect(await within(dialog).findByText(consequence)).toBeInTheDocument();
      expect(within(dialog).queryByText(/This also authorizes/)).not.toBeInTheDocument();
      expect(within(dialog).getAllByText(/stays waiting/)).toHaveLength(1);
    },
  );

  it("says that under web approval the assignment authorizes a machine someone signed in at", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    // The server does not look at the last contact in this case.
    renderWith([machine({ lastSeenUtc: secondsBefore(600), signedInBy: "tech" })], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { body: options({ requireWebApproval: true }) },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      within(dialog).getByText("Last seen from 172.25.132.98 10 minutes ago; signed in by tech."),
    ).toBeInTheDocument();
    expect(
      await within(dialog).findByText(
        "This also authorizes the machine, because tech signed in at it. It then receives the image and the deployment passwords.",
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();
  });

  it("judges a machine waiting at the prompt by the server's clock when the browser's is ahead", async () => {
    // The browser's clock is two minutes ahead of the server's.
    vi.useFakeTimers({ toFake: ["Date"], now: now.getTime() + 120_000 });

    renderWith([machine({ lastSeenUtc: secondsBefore(30) })], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { body: options({ serverUtc: now.toISOString() }) },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      await within(dialog).findByText(
        "This also authorizes the machine, which then receives the image and the deployment passwords.",
      ),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "Last seen from 172.25.132.98 30 seconds ago; nobody has signed in at it.",
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/stays waiting/)).not.toBeInTheDocument();
  });

  it("says only once that a machine reported no disk to install on", async () => {
    renderWith(
      [machine({ state: "Approved", everApproved: true, disks: null, eligibleDiskCount: 0 })],
      operator,
      {
        "GET /api/images": { body: [image({})] },
        "GET /api/deployments/options": { body: options({}) },
      },
    );

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText("Model: Virtual Machine.")).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "The machine reported no disk DDT can install on, so the deployment will fail.",
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/has not reported its disks/)).not.toBeInTheDocument();
  });

  it("does not offer the assignment when the deployment settings cannot be loaded", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now });

    renderWith([machine({ lastSeenUtc: secondsBefore(30) })], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { status: 500, body: { title: "Internal error." } },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      await within(dialog).findByText(
        "The deployment settings could not be loaded, so the dialog cannot say what the assignment does. Close it and try again.",
      ),
    ).toBeInTheDocument();
    await within(dialog).findByRole("option");
    expect(within(dialog).getByRole("button", { name: "Assign image" })).toBeDisabled();
    expect(
      within(dialog).queryByText(/This also authorizes|stays waiting/),
    ).not.toBeInTheDocument();
  });

  it("requires a computer name when a domain is configured", async () => {
    const approved = machine({ state: "Approved", everApproved: true });
    const { calls } = renderWith([approved], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { body: options({ domainConfigured: true }) },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      await within(dialog).findByText(/Required, because machines join the domain/),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(within(dialog).getByRole("button", { name: "Assign image" })).toBeEnabled();
    });

    fireEvent.submit(within(dialog).getByRole("button", { name: "Assign image" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Enter a computer name. Machines join the domain under this name.",
    );
    expect(calls.some((call) => call.method === "POST")).toBe(false);
  });

  it("shows why the server refused an assignment", async () => {
    const approved = machine({ state: "Approved", everApproved: true });
    renderWith([approved], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { body: options({}) },
      [`POST /api/machines/${approved.id}/deployments`]: {
        status: 409,
        body: { title: "The machine already has a deployment." },
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => {
      expect(within(dialog).getByRole("button", { name: "Assign image" })).toBeEnabled();
    });

    fireEvent.click(within(dialog).getByRole("button", { name: "Assign image" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "The machine already has a deployment.",
    );
  });

  it("does not assign to a machine with more than one disk", async () => {
    renderWith([machine({ state: "Approved", eligibleDiskCount: 2 })], operator, {
      "GET /api/images": { body: [image({})] },
      "GET /api/deployments/options": { body: options({}) },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Assign" }));
    const dialog = await screen.findByRole("dialog");

    expect(
      within(dialog).getByText(
        "This machine has more than one disk. Sign in at it and choose the disk there.",
      ),
    ).toBeInTheDocument();
    await within(dialog).findByRole("option");
    expect(within(dialog).getByRole("button", { name: "Assign image" })).toBeDisabled();
  });

  it("stops a running deployment after saying what that leaves behind", async () => {
    const deploying = machine({
      state: "Deploying",
      everApproved: true,
      deployment: deployment({ state: "Running", step: "Apply", percent: 10 }),
    });
    const { calls } = renderWith([deploying], operator, {
      [`DELETE /api/machines/${deploying.id}/deployments/current`]: {
        body: {
          ...deploying,
          state: "Failed",
          deployment: deployment({
            state: "Failed",
            step: "Apply",
            error: "Stopped by operator.",
          }),
        },
      },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Stop" }));
    const dialog = await screen.findByRole("dialog", { name: "Stop the deployment?" });

    expect(
      within(dialog).getByText(
        "This stops the deployment on Virtual Machine (00:15:5D:01:02:03). Its disk is left half written; assign an image again to deploy it.",
      ),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Stop deployment" }));

    expect(await screen.findByText("Stopped by operator.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(calls).toContainEqual({
      method: "DELETE",
      path: `/api/machines/${deploying.id}/deployments/current`,
      body: null,
    });
  });

  it("closes the stop confirmation when the deployment it was opened for ends", async () => {
    const deploying = machine({
      state: "Deploying",
      everApproved: true,
      deployment: deployment({ state: "Running", step: "Apply", percent: 10 }),
    });
    const { calls, queryClient } = renderWith([deploying], operator);

    fireEvent.click(await screen.findByRole("button", { name: "Stop" }));
    await screen.findByRole("dialog", { name: "Stop the deployment?" });

    // Meanwhile the agent reported that the same run failed.
    act(() => {
      upsertMachine(queryClient, {
        ...deploying,
        state: "Failed",
        deployment: deployment({
          state: "Failed",
          step: "Apply",
          error: "The image could not be applied.",
        }),
      });
    });

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.getByText("The image could not be applied.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Stop" })).not.toBeInTheDocument();
    expect(calls.some((call) => call.method === "DELETE")).toBe(false);
  });

  it("closes the stop confirmation when another deployment replaced the one it was opened for", async () => {
    const deploying = machine({
      state: "Deploying",
      everApproved: true,
      deployment: deployment({ state: "Running", step: "Apply", percent: 10 }),
    });
    const { calls, queryClient } = renderWith([deploying], operator);

    fireEvent.click(await screen.findByRole("button", { name: "Stop" }));
    await screen.findByRole("dialog", { name: "Stop the deployment?" });

    // Meanwhile the run failed, and a technician picked another image at the machine.
    act(() => {
      upsertMachine(queryClient, {
        ...deploying,
        deployment: deployment({
          id: "0193a4b2-0000-7000-8000-0000000000d2",
          state: "Running",
          step: "Partition",
          source: "Console",
          requestedBy: "tech",
        }),
      });
    });

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(calls.some((call) => call.method === "DELETE")).toBe(false);

    // The new deployment can be stopped after its own confirmation.
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    expect(await screen.findByRole("dialog", { name: "Stop the deployment?" })).toBeInTheDocument();
  });

  it("cancels an assigned deployment", async () => {
    const approved = machine({
      state: "Approved",
      everApproved: true,
      deployment: deployment({ state: "Assigned" }),
    });
    const { calls } = renderWith([approved], operator, {
      [`DELETE /api/machines/${approved.id}/deployments/current`]: {
        body: { ...approved, deployment: deployment({ state: "Cancelled" }) },
      },
    });

    expect(await screen.findByText("Assigned by operator")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(await screen.findByText(/^Cancelled/)).toBeInTheDocument();
    expect(calls).toContainEqual({
      method: "DELETE",
      path: `/api/machines/${approved.id}/deployments/current`,
      body: null,
    });
    expect(screen.getByRole("button", { name: "Assign" })).toBeInTheDocument();
  });

  it("offers no deployment actions to viewers", async () => {
    renderWith([machine({ state: "Deploying", deployment: deployment({ state: "Running" }) })], {
      ...operator,
      roles: ["Viewer"],
    });

    expect(await screen.findByText("Virtual Machine")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Stop" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();
  });
});
