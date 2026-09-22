// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";

import { ReplacedAnchorBanner } from "./ReplacedAnchorBanner";
import type { ServerCertificateView } from "./serverCertificate";

const certificate: ServerCertificateView = {
  managedByDdt: true,
  subject: "CN=ddt",
  sha256: "b".repeat(64),
  notAfter: "2026-12-20T10:00:00Z",
  renewsUtc: "2026-11-20T10:00:00Z",
  names: ["ddt.example"],
  rootSubject: "CN=DDT root",
  rootSha256: "a".repeat(64),
  rootNotAfter: "2046-09-16T10:00:00Z",
  anchorReplacedUtc: "2026-09-16T10:00:00Z",
};

function user(role: string): CurrentUser {
  return {
    id: "u",
    userName: "someone",
    displayName: null,
    source: "Local",
    twoFactorEnabled: false,
    roles: [role],
  };
}

function renderFor(current: CurrentUser) {
  const calls: string[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = (input instanceof Request ? input.url : input.toString()).replace(
        "http://localhost",
        "",
      );
      const call = `${init?.method ?? "GET"} ${path}`;
      calls.push(call);

      switch (call) {
        case "GET /api/auth/me":
          return Promise.resolve(new Response(JSON.stringify(current), { status: 200 }));
        case "GET /api/server/certificate":
          return Promise.resolve(new Response(JSON.stringify(certificate), { status: 200 }));
        case "DELETE /api/server/certificate/replaced-anchor":
          return Promise.resolve(new Response(null, { status: 204 }));
        default:
          return Promise.resolve(new Response(null, { status: 401 }));
      }
    }),
  );

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <ReplacedAnchorBanner />
    </QueryClientProvider>,
  );

  return { calls };
}

describe("ReplacedAnchorBanner", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells an administrator to build the boot images again, and ends once they confirm it", async () => {
    const { calls } = renderFor(user("Administrator"));

    const banner = await screen.findByRole("region", { name: "Build every boot image again" });
    expect(banner).toHaveTextContent("/var/lib/ddt/certs/ddt-root.pem");
    expect(banner).toHaveTextContent("a".repeat(64));

    fireEvent.click(within(banner).getByRole("button", { name: "Done" }));
    const dialog = await screen.findByRole("dialog", { name: "Every boot image is built again?" });

    expect(calls).not.toContain("DELETE /api/server/certificate/replaced-anchor");

    fireEvent.click(
      within(dialog).getByRole("button", { name: "Every boot image is built again" }),
    );

    await waitFor(() => {
      expect(
        screen.queryByRole("region", { name: "Build every boot image again" }),
      ).not.toBeInTheDocument();
    });
    expect(calls).toContain("DELETE /api/server/certificate/replaced-anchor");
  });

  it("shows operators nothing and does not ask for the certificate", async () => {
    const { calls } = renderFor(user("Operator"));

    await waitFor(() => {
      expect(calls).toContain("GET /api/auth/me");
    });

    expect(screen.queryByRole("region")).not.toBeInTheDocument();
    expect(calls).not.toContain("GET /api/server/certificate");
  });
});
