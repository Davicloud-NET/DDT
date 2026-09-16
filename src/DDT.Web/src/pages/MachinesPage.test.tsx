import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { MachineSummary } from "@/machines/machines";

import { MachinesPage } from "./MachinesPage";

function renderWith(machines: MachineSummary[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();
      const body = url.endsWith("/api/machines") ? machines : null;

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
}

describe("MachinesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("explains that no machine has registered yet", async () => {
    renderWith([]);

    expect(screen.getByRole("heading", { level: 1, name: "Machines" })).toBeInTheDocument();
    expect(
      await screen.findByRole("heading", { level: 2, name: "No machines yet" }),
    ).toBeInTheDocument();
  });

  it("lists a registered machine with its state and MAC", async () => {
    renderWith([
      {
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
        signedInBy: "bob",
      },
    ]);

    expect(await screen.findByText("Virtual Machine")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
    expect(screen.getByText("00:15:5D:01:02:03")).toBeInTheDocument();
    expect(screen.getByText("Signed in by bob")).toBeInTheDocument();
  });
});
