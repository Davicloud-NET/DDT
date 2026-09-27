// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  administrator,
  json,
  noContent,
  operator,
  problem,
  reads,
  servePage,
  stubClipboard,
  type Handler,
} from "@/test/serve";
import type { ApiTokenView } from "@/tokens/tokens";

import type { CreateUserRequest, DirectoryView, UserView } from "./users";
import { UsersPage } from "./UsersPage";

function account(overrides: Partial<UserView>): UserView {
  return {
    id: "0193a4b2-0000-7000-8000-00000000b001",
    userName: "m.huber",
    displayName: "Maria Huber",
    email: "m.huber@corp.example",
    source: "Local",
    role: "Operator",
    roleFrom: "Manual",
    disabled: false,
    lockedOutUntil: null,
    twoFactorEnabled: true,
    hasPassword: true,
    mustChangePassword: false,
    externalProvider: null,
    createdUtc: "2026-09-01T10:00:00Z",
    lastSignInUtc: new Date(Date.now() - 3 * 3_600_000).toISOString(),
    ...overrides,
  };
}

const me = account({
  id: administrator.id,
  userName: "admin",
  displayName: "Ada Admin",
  email: null,
  role: "Administrator",
  twoFactorEnabled: false,
});
const maria = account({});
const directoryUser = account({
  id: "0193a4b2-0000-7000-8000-00000000b002",
  userName: "j.berger",
  displayName: "Jonas Berger",
  email: "j.berger@corp.example",
  source: "Directory",
  role: "Viewer",
  roleFrom: "DirectoryGroups",
  twoFactorEnabled: false,
  hasPassword: false,
  lastSignInUtc: null,
});
const singleSignOn = account({
  id: "0193a4b2-0000-7000-8000-00000000b003",
  userName: "k.novak@corp.example",
  displayName: null,
  email: null,
  source: "External",
  role: null,
  roleFrom: null,
  twoFactorEnabled: false,
  hasPassword: false,
  externalProvider: "Entra ID",
  disabled: true,
  lockedOutUntil: new Date(Date.now() + 600_000).toISOString(),
});

const directoryOff: DirectoryView = { enabled: false, host: null, baseDn: null, groupRoleMap: [] };
const directoryOn: DirectoryView = {
  enabled: true,
  host: "dc01.corp.example",
  baseDn: "DC=corp,DC=example",
  groupRoleMap: [
    {
      group: "CN=DDT Admins,OU=Groups,DC=corp,DC=example",
      name: "DDT Admins",
      role: "Administrator",
    },
    { group: "CN=Gone,OU=Groups,DC=corp,DC=example", name: null, role: "Viewer" },
  ],
};

function serve(handlers: Record<string, Handler>, user = administrator) {
  return servePage({
    user,
    path: "/admin/users",
    component: UsersPage,
    handlers: {
      "GET /api/users": () => json([me, directoryUser, singleSignOn, maria]),
      "GET /api/directory": () => json(directoryOff),
      ...handlers,
    },
  });
}

function row(name: string): HTMLElement {
  const found = screen.getAllByText(name)[0]?.closest<HTMLElement>("[role=row]") ?? null;

  if (found === null) {
    throw new Error(`${name} is not in a table row.`);
  }

  return found;
}

// Opens the account's menu and picks an action in it.
async function pick(userName: string, action: string) {
  fireEvent.click(await screen.findByRole("button", { name: `Actions for ${userName}` }));
  const menu = await screen.findByRole("menu", { name: `Actions for ${userName}` });
  fireEvent.click(within(menu).getByRole("menuitem", { name: action }));
}

describe("UsersPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("tells someone who is no administrator that only administrators manage accounts, and asks for nothing", async () => {
    const { requests } = serve({}, operator);

    expect(await screen.findByText(/^Only administrators manage accounts\./)).toBeInTheDocument();
    expect(requests.some((request) => request.path.startsWith("/api/users"))).toBe(false);
    expect(requests.some((request) => request.path.startsWith("/api/directory"))).toBe(false);
  });

  it("lists each account with how it signs in, its role and where that comes from, its second factor and state", async () => {
    serve({});

    await screen.findByText("Maria Huber");

    const local = row("Maria Huber");
    expect(local).toHaveTextContent("m.huber · m.huber@corp.example");
    expect(local).toHaveTextContent("Local account");
    expect(local).toHaveTextContent("OperatorSet by an administrator");
    expect(local).toHaveTextContent("On");
    expect(local).toHaveTextContent("Active");
    expect(local).toHaveTextContent("3 hours ago");

    const directory = row("Jonas Berger");
    expect(directory).toHaveTextContent("Directory account");
    expect(directory).toHaveTextContent("ViewerFrom its directory groups");
    expect(directory).toHaveTextContent("Never");

    const external = row("k.novak@corp.example");
    expect(external).toHaveTextContent("Single sign-on accountEntra ID");
    expect(external).toHaveTextContent("No roleReaches nothing");
    expect(within(external).getByText("Disabled")).toBeInTheDocument();
    expect(within(external).getByText("Locked")).toBeInTheDocument();

    expect(row("Ada Admin")).toHaveTextContent("you");
  });

  it("narrows the list by what is typed", async () => {
    serve({});

    await screen.findByText("Maria Huber");
    fireEvent.change(screen.getByRole("searchbox", { name: "Find an account" }), {
      target: { value: "corp.example" },
    });

    await waitFor(() => {
      expect(screen.queryByText("Ada Admin")).not.toBeInTheDocument();
    });
    expect(screen.getByText("Maria Huber")).toBeInTheDocument();
    expect(screen.getByText("Jonas Berger")).toBeInTheDocument();

    fireEvent.change(screen.getByRole("searchbox", { name: "Find an account" }), {
      target: { value: "nobody like that" },
    });
    expect(await screen.findByText("No account matches")).toBeInTheDocument();
  });

  it("offers no disabling, reset or deletion of the own account", async () => {
    serve({});

    fireEvent.click(await screen.findByRole("button", { name: "Actions for admin" }));
    const menu = await screen.findByRole("menu", { name: "Actions for admin" });

    expect(
      within(menu)
        .getAllByRole("menuitem")
        .map((item) => item.textContent),
    ).toEqual(["Change name, email or role"]);
  });

  it("adds an account, puts it in the list without reading the list again, and shows its password once", async () => {
    const clipboard = stubClipboard();
    let sent: CreateUserRequest | null = null;
    const created = account({
      id: "0193a4b2-0000-7000-8000-00000000b009",
      userName: "p.lang",
      displayName: "Paul Lang",
      email: null,
      role: "Operator",
      twoFactorEnabled: false,
      mustChangePassword: true,
      lastSignInUtc: null,
    });
    const { requests, queryClient } = serve({
      "POST /api/users": (request) => {
        sent = request.body as CreateUserRequest;
        return json({ user: created, password: "Tq8v-Rk3m-Wz6p-Hd2n" }, 201);
      },
    });

    await screen.findByText("Maria Huber");
    const readsBefore = reads(requests, "/api/users");
    fireEvent.click(screen.getByRole("button", { name: "Add account" }));

    const dialog = await screen.findByRole("dialog", { name: "Add a local account" });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "User name" }), {
      target: { value: " p.lang " },
    });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "Name" }), {
      target: { value: "Paul Lang" },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: /Role$/ }));
    fireEvent.click(await screen.findByRole("option", { name: /^Operator/ }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Add account" }));

    const shown = await screen.findByRole("dialog", { name: "Password for Paul Lang" });
    expect(sent).toEqual({
      userName: "p.lang",
      displayName: "Paul Lang",
      email: null,
      role: "Operator",
    });
    expect(within(shown).getByText("Tq8v-Rk3m-Wz6p-Hd2n")).toBeInTheDocument();
    expect(shown).toHaveTextContent(
      "DDT shows this password only now. At the next sign-in, Paul Lang has to set a password of their own, and can do nothing else until then.",
    );

    fireEvent.click(within(shown).getByRole("button", { name: "Copy" }));
    await waitFor(() => {
      expect(clipboard.written).toEqual(["Tq8v-Rk3m-Wz6p-Hd2n"]);
    });
    expect(within(shown).getByRole("button", { name: "Copied" })).toBeInTheDocument();

    fireEvent.click(within(shown).getByRole("button", { name: "Done" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(screen.queryByText("Tq8v-Rk3m-Wz6p-Hd2n")).not.toBeInTheDocument();
    expect(row("Paul Lang")).toHaveTextContent("New password due");
    expect(reads(requests, "/api/users")).toBe(readsBefore);
    expect(queryClient.getQueryData<UserView[]>(["users"])?.map((user) => user.userName)).toEqual([
      "admin",
      "j.berger",
      "k.novak@corp.example",
      "m.huber",
      "p.lang",
    ]);
  });

  it("shows the server's refusal of a user name at the field", async () => {
    serve({
      "POST /api/users": () =>
        problem("Invalid", 400, { userName: ["There is an account named m.huber already."] }),
    });

    await screen.findByText("Maria Huber");
    fireEvent.click(screen.getByRole("button", { name: "Add account" }));
    const dialog = await screen.findByRole("dialog", { name: "Add a local account" });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "User name" }), {
      target: { value: "m.huber" },
    });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "Name" }), {
      target: { value: "Maria" },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Add account" }));

    await waitFor(() => {
      expect(
        within(dialog).getByRole("textbox", { name: "User name" }),
      ).toHaveAccessibleDescription(/There is an account named m\.huber already\./);
    });
  });

  it("gives an account a new password, shows it once and marks the account, without reading the list again", async () => {
    const { requests } = serve({
      [`POST /api/users/${maria.id}/reset-password`]: () =>
        json({ password: "Nw4c-Lp8x-Qe2r-Jm5t" }),
    });

    await screen.findByText("Maria Huber");
    const readsBefore = reads(requests, "/api/users");
    await pick("m.huber", "Reset password");

    const confirm = await screen.findByRole("dialog", { name: "Give Maria Huber a new password?" });
    expect(confirm).toHaveTextContent(
      "The current password stops working at once, and Maria Huber is signed out within a minute.",
    );
    fireEvent.click(within(confirm).getByRole("button", { name: "Make a new password" }));

    const shown = await screen.findByRole("dialog", { name: "New password for Maria Huber" });
    expect(within(shown).getByText("Nw4c-Lp8x-Qe2r-Jm5t")).toBeInTheDocument();
    expect(row("Maria Huber")).toHaveTextContent("New password due");

    fireEvent.click(within(shown).getByRole("button", { name: "Done" }));
    await waitFor(() => {
      expect(screen.queryByText("Nw4c-Lp8x-Qe2r-Jm5t")).not.toBeInTheDocument();
    });
    expect(reads(requests, "/api/users")).toBe(readsBefore);
  });

  it("changes only what was changed, and patches the row with the answer", async () => {
    let sent: unknown = null;
    const { requests } = serve({
      [`PATCH /api/users/${maria.id}`]: (request) => {
        sent = request.body;
        return json({ ...maria, role: "Administrator" });
      },
    });

    await screen.findByText("Maria Huber");
    await pick("m.huber", "Change name, email or role");
    const dialog = await screen.findByRole("dialog", { name: "Change Maria Huber" });
    expect(within(dialog).getByRole("button", { name: "Save changes" })).toBeDisabled();

    fireEvent.click(within(dialog).getByRole("button", { name: /Role$/ }));
    fireEvent.click(await screen.findByRole("option", { name: /^Administrator/ }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(sent).toEqual({ role: "Administrator" });
    expect(row("Maria Huber")).toHaveTextContent("AdministratorSet by an administrator");
    expect(reads(requests, "/api/users")).toBe(1);
  });

  it("locks the role and the directory's fields of an account whose groups decide them, and says where to change them", async () => {
    serve({});

    await screen.findByText("Jonas Berger");
    await pick("j.berger", "Change name, email or role");
    const dialog = await screen.findByRole("dialog", { name: "Change Jonas Berger" });

    expect(within(dialog).getByRole("textbox", { name: "Name" })).toBeDisabled();
    expect(within(dialog).getByRole("textbox", { name: "Email address" })).toBeDisabled();
    expect(dialog).toHaveTextContent(
      "The role of j.berger comes from its directory groups, through the map on the Sign-in page, at each sign-in. Change its groups in the directory, or the map.",
    );
    expect(within(dialog).getByRole("link", { name: "Sign-in" })).toHaveAttribute(
      "href",
      "/admin/sign-in",
    );
    expect(within(dialog).getByRole("button", { name: /Role$/ })).toBeDisabled();
  });

  it("shows a refusal of a change in the dialog", async () => {
    serve({
      [`PATCH /api/users/${maria.id}`]: () =>
        problem(
          "m.huber is the last enabled administrator. Make another account an administrator first.",
          409,
        ),
    });

    await screen.findByText("Maria Huber");
    await pick("m.huber", "Change name, email or role");
    const dialog = await screen.findByRole("dialog", { name: "Change Maria Huber" });
    fireEvent.change(within(dialog).getByRole("textbox", { name: "Name" }), {
      target: { value: "Maria H." },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "m.huber is the last enabled administrator. Make another account an administrator first.",
    );
  });

  it("disables an account after asking and enables it again from the answer", async () => {
    const { requests } = serve({
      [`POST /api/users/${maria.id}/disable`]: () => json({ ...maria, disabled: true }),
      [`POST /api/users/${maria.id}/enable`]: () => json({ ...maria, disabled: false }),
    });

    await screen.findByText("Maria Huber");
    await pick("m.huber", "Disable");
    const dialog = await screen.findByRole("dialog", { name: "Disable Maria Huber?" });
    expect(dialog).toHaveTextContent("its API tokens stop working");
    fireEvent.click(within(dialog).getByRole("button", { name: "Disable account" }));

    await waitFor(() => {
      expect(within(row("Maria Huber")).getByText("Disabled")).toBeInTheDocument();
    });

    await pick("m.huber", "Enable");
    await waitFor(() => {
      expect(within(row("Maria Huber")).queryByText("Disabled")).not.toBeInTheDocument();
    });
    expect(reads(requests, "/api/users")).toBe(1);
  });

  it("says a deleted directory account comes back, and drops it and its tokens without reading the lists again", async () => {
    const token: ApiTokenView = {
      id: "t1",
      name: "Inventory",
      role: "Viewer",
      userId: directoryUser.id,
      userName: directoryUser.userName,
      hint: "abcd",
      createdUtc: "2026-09-01T10:00:00Z",
      expiresUtc: "2026-12-01T10:00:00Z",
      lastUsedUtc: null,
      lastUsedAddress: null,
      revokedUtc: null,
      revokedByName: null,
    };
    const { requests, queryClient } = serve({
      [`DELETE /api/users/${directoryUser.id}`]: () => noContent(),
    });
    queryClient.setQueryData(["tokens", "all"], [token]);

    await screen.findByText("Jonas Berger");
    await pick("j.berger", "Delete");
    const dialog = await screen.findByRole("dialog", { name: "Delete Jonas Berger?" });
    expect(dialog).toHaveTextContent(
      "j.berger and its API tokens are deleted from DDT. The audit log keeps its entries. If its directory groups still give it a role, it comes back at its next sign-in as a new account; to keep it out, disable it instead.",
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete account" }));

    await waitFor(() => {
      expect(screen.queryByText("Jonas Berger")).not.toBeInTheDocument();
    });
    expect(queryClient.getQueryData(["tokens", "all"])).toEqual([]);
    expect(reads(requests, "/api/users")).toBe(1);
    expect(reads(requests, "/api/tokens/all")).toBe(0);
  });

  it("turns off a second factor from the answer", async () => {
    serve({
      [`POST /api/users/${maria.id}/reset-two-factor`]: () =>
        json({ ...maria, twoFactorEnabled: false }),
    });

    await screen.findByText("Maria Huber");
    await pick("m.huber", "Reset second factor");
    const dialog = await screen.findByRole("dialog", {
      name: "Turn off the second factor of Maria Huber?",
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Turn off second factor" }));

    await waitFor(() => {
      expect(row("Maria Huber")).toHaveTextContent("Off");
    });
  });

  it("says when sign-in through a directory is off, and where it is turned on", async () => {
    serve({});

    expect(await screen.findByText(/^Sign-in through a directory is off\./)).toHaveTextContent(
      "Sign-in through a directory is off. It is turned on and set up on the Sign-in page.",
    );
    expect(screen.getByRole("link", { name: "Sign-in" })).toHaveAttribute("href", "/admin/sign-in");
    expect(
      screen.queryByRole("searchbox", { name: "Find a directory group" }),
    ).not.toBeInTheDocument();
  });

  it("shows the group map, finds groups once typing rests, and says what the directory answered", async () => {
    const { requests } = serve({
      "GET /api/directory": () => json(directoryOn),
      "GET /api/directory/groups?query=ddt&limit=20": () =>
        json([
          {
            distinguishedName: "CN=DDT Admins,OU=Groups,DC=corp,DC=example",
            name: "DDT Admins",
            description: "Deployment administrators",
          },
          {
            distinguishedName: "CN=DDT Lab,OU=Groups,DC=corp,DC=example",
            name: null,
            description: null,
          },
        ]),
      "GET /api/directory/groups?query=down&limit=20": () =>
        problem("The directory at dc01.corp.example did not answer.", 502),
    });

    const map = await screen.findByRole("list", { name: "Groups and the roles they give" });
    expect(map).toHaveTextContent(
      "DDT AdminsCN=DDT Admins,OU=Groups,DC=corp,DC=exampleAdministrator",
    );
    expect(map).toHaveTextContent(
      "Not found in the directoryCN=Gone,OU=Groups,DC=corp,DC=exampleViewer",
    );
    expect(screen.getByText(/^At each sign-in, a directory account/)).toHaveTextContent(
      "an account in none of them cannot sign in. The map is set on the Sign-in page.",
    );

    vi.useFakeTimers({ shouldAdvanceTime: true });
    const search = screen.getByRole("searchbox", { name: "Find a directory group" });
    fireEvent.change(search, { target: { value: "d" } });
    fireEvent.change(search, { target: { value: "dd" } });
    fireEvent.change(search, { target: { value: "ddt" } });
    await act(() => vi.advanceTimersByTimeAsync(400));

    const found = await screen.findByRole("list", { name: "Directory groups found" });
    expect(found).toHaveTextContent("DDT AdminsGives Administrator");
    expect(found).toHaveTextContent("Deployment administrators");
    expect(found).toHaveTextContent("CN=DDT Lab,OU=Groups,DC=corp,DC=example");
    expect(
      requests.filter((request) => request.path.startsWith("/api/directory/groups")),
    ).toHaveLength(1);

    fireEvent.change(search, { target: { value: "down" } });
    await act(() => vi.advanceTimersByTimeAsync(400));
    expect(
      await screen.findByText("The directory at dc01.corp.example did not answer."),
    ).toBeInTheDocument();
  });

  it("checks what a sign-in would give a user", async () => {
    serve({
      "GET /api/directory": () => json(directoryOn),
      "POST /api/directory/check": () =>
        json({
          found: true,
          distinguishedName: "CN=Jonas Berger,OU=Staff,DC=corp,DC=example",
          displayName: "Jonas Berger",
          groups: [
            "CN=DDT Admins,OU=Groups,DC=corp,DC=example",
            "CN=Staff,OU=Groups,DC=corp,DC=example",
          ],
          matches: [{ group: "CN=DDT Admins,OU=Groups,DC=corp,DC=example", role: "Administrator" }],
          role: "Administrator",
          message: "j.berger gets Administrator from DDT Admins.",
        }),
    });

    const field = await screen.findByRole("textbox", { name: "User name" });
    fireEvent.change(field, { target: { value: "j.berger" } });
    fireEvent.click(screen.getByRole("button", { name: "Check" }));

    expect(
      await screen.findByText("j.berger gets Administrator from DDT Admins."),
    ).toBeInTheDocument();
    expect(screen.getByText("DDT Admins gives Administrator")).toBeInTheDocument();
    expect(screen.getByText("CN=Jonas Berger,OU=Staff,DC=corp,DC=example")).toBeInTheDocument();
    expect(screen.getByText("2 groups")).toBeInTheDocument();
  });

  it("shows why the directory could not be asked", async () => {
    serve({
      "GET /api/directory": () => json(directoryOn),
      "POST /api/directory/check": () =>
        problem(
          "The directory connection is not complete. Set DDT:Ldap:Host and DDT:Ldap:BaseDn.",
          409,
        ),
    });

    fireEvent.change(await screen.findByRole("textbox", { name: "User name" }), {
      target: { value: "j.berger" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Check" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The directory connection is not complete. Set DDT:Ldap:Host and DDT:Ldap:BaseDn.",
    );
  });
});
