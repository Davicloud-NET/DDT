// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import {
  json,
  noContent,
  operator,
  reads,
  servePage,
  stubClipboard,
  type Handler,
} from "@/test/serve";
import type { ApiTokenView, CreateApiTokenRequest } from "@/tokens/tokens";

import { AccountPage } from "./AccountPage";

const day = 86_400_000;

const reporting: ApiTokenView = {
  id: "0193a4b2-0000-7000-8000-00000000c101",
  name: "Reporting",
  role: "Viewer",
  userId: operator.id,
  userName: "operator",
  hint: "Zz90",
  createdUtc: new Date(Date.now() - 2 * day).toISOString(),
  expiresUtc: new Date(Date.now() + 88 * day + 60_000).toISOString(),
  lastUsedUtc: null,
  lastUsedAddress: null,
  revokedUtc: null,
  revokedByName: null,
};

function serve(handlers: Record<string, Handler>, user: CurrentUser = operator) {
  return servePage({
    user,
    path: "/account",
    component: AccountPage,
    handlers: { "GET /api/tokens": () => json([reporting]), ...handlers },
  });
}

describe("AccountPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists the person's own tokens", async () => {
    serve({});

    const table = await screen.findByRole("grid", { name: "Your API tokens" });
    expect(table).toHaveTextContent("Reporting");
    expect(table).toHaveTextContent("ddt_…Zz90");
    expect(table).toHaveTextContent("in 88 days");
    expect(within(table).getByRole("button", { name: "Revoke Reporting" })).toBeInTheDocument();
  });

  it("makes a token with no more than the person's role, shows its secret once, and lists it without the secret", async () => {
    const clipboard = stubClipboard();
    let sent: CreateApiTokenRequest | null = null;
    const made: ApiTokenView = {
      ...reporting,
      id: "0193a4b2-0000-7000-8000-00000000c102",
      name: "Nightly sync",
      role: "Operator",
      hint: "9fKe",
      createdUtc: new Date().toISOString(),
    };
    const { requests, queryClient } = serve({
      "POST /api/tokens": (request) => {
        sent = request.body as CreateApiTokenRequest;
        return json({ token: made, secret: "ddt_3sQv9LkPz2Wm8Rt4Yx6Nb1Hc7Jd5Fg09fKe" }, 201);
      },
    });

    await screen.findByText("Reporting");
    fireEvent.click(screen.getByRole("button", { name: "Make a token" }));
    const dialog = await screen.findByRole("dialog", { name: "Make an API token" });

    fireEvent.click(within(dialog).getByRole("button", { name: /Role$/ }));
    const options = (await screen.findAllByRole("option")).map((option) =>
      option.getAttribute("data-key"),
    );
    expect(options).toEqual(["Operator", "Viewer"]);
    fireEvent.click(screen.getByRole("option", { name: /^Operator/ }));

    fireEvent.change(within(dialog).getByRole("textbox", { name: "Name" }), {
      target: { value: "Nightly sync" },
    });
    const lifetime = within(dialog).getByRole("textbox", { name: "Lifetime in days" });
    fireEvent.change(lifetime, { target: { value: "30" } });
    fireEvent.blur(lifetime);
    fireEvent.click(within(dialog).getByRole("button", { name: "Make token" }));

    const shown = await screen.findByRole("dialog", { name: "Copy the token Nightly sync" });
    expect(sent).toEqual({ name: "Nightly sync", role: "Operator", expiresInDays: 30 });
    expect(within(shown).getByText("ddt_3sQv9LkPz2Wm8Rt4Yx6Nb1Hc7Jd5Fg09fKe")).toBeInTheDocument();
    expect(shown).toHaveTextContent(
      "DDT shows this token only now and keeps nothing but a hash of it.",
    );
    fireEvent.click(within(shown).getByRole("button", { name: "Copy" }));
    await waitFor(() => {
      expect(clipboard.written).toEqual(["ddt_3sQv9LkPz2Wm8Rt4Yx6Nb1Hc7Jd5Fg09fKe"]);
    });

    fireEvent.click(within(shown).getByRole("button", { name: "Done" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.queryByText(/ddt_3sQv9/)).not.toBeInTheDocument();
    expect(screen.getByText("Nightly sync")).toBeInTheDocument();
    expect(screen.getByText("ddt_…9fKe")).toBeInTheDocument();
    expect(JSON.stringify(queryClient.getQueryData(["tokens", "own"]))).not.toContain("3sQv9");
    expect(reads(requests, "/api/tokens")).toBe(1);
  });

  it("shows the server's refusal of a token name at the field", async () => {
    serve({
      "POST /api/tokens": () =>
        json(
          {
            title: "Invalid",
            errors: {
              name: [
                "You have a token of that name already. Choose another name, or revoke that token first.",
              ],
            },
          },
          400,
        ),
    });

    await screen.findByText("Reporting");
    fireEvent.click(screen.getByRole("button", { name: "Make a token" }));
    const dialog = await screen.findByRole("dialog", { name: "Make an API token" });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "Name" }), {
      target: { value: "Reporting" },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Make token" }));

    await waitFor(() => {
      expect(within(dialog).getByRole("textbox", { name: "Name" })).toHaveAccessibleDescription(
        /You have a token of that name already\./,
      );
    });
  });

  it("revokes an own token after asking, without reading the list again", async () => {
    const { requests } = serve({ [`DELETE /api/tokens/${reporting.id}`]: () => noContent() });

    fireEvent.click(await screen.findByRole("button", { name: "Revoke Reporting" }));
    const dialog = await screen.findByRole("dialog", { name: "Revoke Reporting?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Revoke token" }));

    await waitFor(() => {
      expect(screen.getByText("Revoked")).toBeInTheDocument();
    });
    expect(screen.queryByRole("button", { name: "Revoke Reporting" })).not.toBeInTheDocument();
    expect(reads(requests, "/api/tokens")).toBe(1);
  });

  it("asks an account with a password it was given to set its own first, and lets it go on once it has", async () => {
    let changed: unknown = null;
    const { requests, queryClient } = serve(
      {
        "POST /api/auth/password": (request) => {
          changed = request.body;
          return new Response(null, { status: 200, headers: { "Content-Length": "0" } });
        },
      },
      { ...operator, mustChangePassword: true },
    );

    expect(await screen.findByText("Set a password of your own first")).toBeInTheDocument();
    expect(screen.queryByText("API tokens")).not.toBeInTheDocument();
    expect(requests.some((request) => request.path === "/api/tokens")).toBe(false);

    fireEvent.change(screen.getByLabelText("Current password"), {
      target: { value: "Tq8v-Rk3m-Wz6p-Hd2n" },
    });
    fireEvent.change(screen.getByLabelText("New password"), {
      target: { value: "a long one of my own" },
    });
    fireEvent.change(screen.getByLabelText("New password again"), {
      target: { value: "a long one of my own" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(
      await screen.findByText("Password changed. The rest of DDT is open to you now."),
    ).toBeInTheDocument();
    expect(changed).toEqual({
      currentPassword: "Tq8v-Rk3m-Wz6p-Hd2n",
      newPassword: "a long one of my own",
    });
    expect(screen.queryByText("Set a password of your own first")).not.toBeInTheDocument();
    expect(queryClient.getQueryData<CurrentUser>(["current-user"])?.mustChangePassword).toBe(false);
    expect(await screen.findByText("Reporting")).toBeInTheDocument();
  });
});
