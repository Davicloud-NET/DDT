// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  administrator,
  json,
  noContent,
  operator,
  problem,
  reads,
  servePage,
  type Handler,
} from "@/test/serve";

import type { ApiTokenView } from "./tokens";
import { TokensPage } from "./TokensPage";

const day = 86_400_000;

function token(overrides: Partial<ApiTokenView>): ApiTokenView {
  return {
    id: "0193a4b2-0000-7000-8000-00000000c001",
    name: "Inventory",
    role: "Viewer",
    userId: operator.id,
    userName: "operator",
    hint: "x7Qa",
    createdUtc: new Date(Date.now() - 10 * day).toISOString(),
    expiresUtc: new Date(Date.now() + 30 * day + 60_000).toISOString(),
    lastUsedUtc: new Date(Date.now() - 2 * 3_600_000).toISOString(),
    lastUsedAddress: "10.20.4.17",
    revokedUtc: null,
    revokedByName: null,
    ...overrides,
  };
}

const inventory = token({});
const backup = token({
  id: "0193a4b2-0000-7000-8000-00000000c002",
  name: "Backup job",
  role: "Operator",
  userId: administrator.id,
  userName: "admin",
  lastUsedUtc: null,
  lastUsedAddress: null,
});
const expired = token({
  id: "0193a4b2-0000-7000-8000-00000000c003",
  name: "Old report",
  expiresUtc: new Date(Date.now() - 3 * day).toISOString(),
});
const revoked = token({
  id: "0193a4b2-0000-7000-8000-00000000c004",
  name: "Leaked",
  revokedUtc: new Date(Date.now() - 5 * 60_000).toISOString(),
  revokedByName: "admin",
});

function serve(handlers: Record<string, Handler>, user = administrator) {
  return servePage({
    user,
    path: "/admin/tokens",
    component: TokensPage,
    handlers: {
      "GET /api/tokens/all": () => json([inventory, backup, expired, revoked]),
      ...handlers,
    },
  });
}

function row(name: string): HTMLElement {
  const found = screen.getByText(name).closest<HTMLElement>("[role=row]");

  if (found === null) {
    throw new Error(`${name} is not in a table row.`);
  }

  return found;
}

describe("TokensPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("tells someone who is no administrator where their own tokens are, and asks for nothing", async () => {
    const { requests } = serve({}, operator);

    expect(await screen.findByText(/^Only administrators see every token\./)).toBeInTheDocument();
    expect(requests.some((request) => request.path.startsWith("/api/tokens"))).toBe(false);
  });

  it("lists the tokens that still work, with their user, role, last use and expiry", async () => {
    serve({});

    await screen.findByText("Inventory");
    const used = row("Inventory");

    expect(used).toHaveTextContent("ddt_…x7Qa");
    expect(used).toHaveTextContent("operator");
    expect(used).toHaveTextContent("Viewer");
    expect(used).toHaveTextContent("2 hours ago from 10.20.4.17");
    expect(used).toHaveTextContent("in 30 days");
    expect(within(used).getByText("Active")).toBeInTheDocument();
    expect(within(used).getByText("in 30 days")).toHaveAttribute(
      "title",
      new Date(inventory.expiresUtc).toLocaleString("en"),
    );
    expect(row("Backup job")).toHaveTextContent("Never");
    expect(screen.queryByText("Old report")).not.toBeInTheDocument();
    expect(screen.queryByText("Leaked")).not.toBeInTheDocument();
  });

  it("shows expired and revoked tokens by filter, and finds them by name or user", async () => {
    serve({});

    await screen.findByText("Inventory");
    fireEvent.click(screen.getByRole("radio", { name: /Expired/ }));
    await waitFor(() => {
      expect(screen.queryByText("Inventory")).not.toBeInTheDocument();
    });
    expect(within(row("Old report")).getByText("Expired")).toBeInTheDocument();
    expect(within(row("Old report")).queryByRole("button", { name: /Revoke/ })).toBeNull();

    fireEvent.click(screen.getByRole("radio", { name: /Revoked/ }));
    expect(await screen.findByText("Leaked")).toBeInTheDocument();
    expect(row("Leaked")).toHaveTextContent("5 minutes ago by admin");

    fireEvent.click(screen.getByRole("radio", { name: /All/ }));
    fireEvent.change(screen.getByRole("searchbox", { name: "Find a token" }), {
      target: { value: "admin" },
    });
    await waitFor(() => {
      expect(screen.queryByText("Inventory")).not.toBeInTheDocument();
    });
    expect(screen.getByText("Backup job")).toBeInTheDocument();

    fireEvent.change(screen.getByRole("searchbox", { name: "Find a token" }), {
      target: { value: "nothing like it" },
    });
    expect(await screen.findByText("No token matches")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Show all tokens" }));
    expect(await screen.findByText("Leaked")).toBeInTheDocument();
  });

  it("revokes another user's token after asking, and keeps it in the list as revoked without reading the list again", async () => {
    const { requests, queryClient } = serve({
      [`DELETE /api/tokens/${inventory.id}`]: () => noContent(),
    });

    await screen.findByText("Inventory");
    fireEvent.click(screen.getByRole("button", { name: "Revoke Inventory" }));
    const dialog = await screen.findByRole("dialog", { name: "Revoke Inventory of operator?" });
    expect(dialog).toHaveTextContent("Whatever uses the token is refused from its next request.");
    fireEvent.click(within(dialog).getByRole("button", { name: "Revoke token" }));

    await waitFor(() => {
      expect(screen.queryByText("Inventory")).not.toBeInTheDocument();
    });
    const cached = queryClient
      .getQueryData<ApiTokenView[]>(["tokens", "all"])
      ?.find((entry) => entry.id === inventory.id);
    expect(cached?.revokedUtc).not.toBeNull();
    expect(cached?.revokedByName).toBe("admin");
    expect(reads(requests, "/api/tokens/all")).toBe(1);

    fireEvent.click(screen.getByRole("radio", { name: /Revoked/ }));
    expect(await screen.findByText("Inventory")).toBeInTheDocument();
    expect(row("Inventory")).toHaveTextContent("by admin");
  });

  it("shows a refusal of a revoke in the dialog", async () => {
    serve({
      [`DELETE /api/tokens/${inventory.id}`]: () =>
        problem("Only the token's owner or an administrator can revoke it.", 403),
    });

    await screen.findByText("Inventory");
    fireEvent.click(screen.getByRole("button", { name: "Revoke Inventory" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Revoke token" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Only the token's owner or an administrator can revoke it.",
    );
  });
});
