// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { AccountView, SaveAccountRequest } from "@/accounts/accounts";
import { chooseMenuItem, fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { accountView, administrator, viewer } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes, type Sent } from "@/test/server";

const join = accountView({
  id: "a1",
  name: "Join account",
  userName: "CORP\\ddt-join",
  domain: "corp.example",
  usedBy: [
    {
      sequenceId: "s1",
      sequenceName: "Windows 11 office PCs",
      steps: [{ stepId: "join", stepName: "Join the domain", field: "account" }],
    },
  ],
});

const share = accountView({
  id: "a2",
  name: "Software share",
  userName: "svc-software@corp.example",
  domain: null,
  hosts: ["files.corp.example"],
  runAs: true,
});

const backup = accountView({
  id: "a3",
  name: "Old backup account",
  userName: "CORP\\backup",
  domain: null,
  password: { isSet: true, unreadable: true, updatedUtc: null },
});

const lab = accountView({
  id: "a4",
  name: "Test lab",
  userName: "LAB\\tester",
  domain: null,
  password: { isSet: false, unreadable: false, updatedUtc: null },
});

const secret = "Correct horse 7";

function open(
  accounts: AccountView[],
  routes: Routes = {},
  user = administrator,
): Promise<RenderedPage> {
  return renderPage({
    path: "/deployment/accounts",
    user,
    routes: {
      "GET /api/accounts": { body: accounts },
      "POST /api/settings/reauthenticate": () =>
        json({ token: "proof", expiresUtc: new Date(Date.now() + 300_000).toISOString() }),
      ...routes,
    },
  });
}

function table(): HTMLElement {
  return screen.getByRole("grid", { name: "Accounts", hidden: true });
}

function row(name: string): HTMLElement {
  return within(table()).getByRole("row", { name: new RegExp(`^${name}`), hidden: true });
}

// The server asks for the person's password again for every change that doesn't carry a recent proof.
function reauthenticate(): Response {
  return json(
    {
      title: "Enter your password again to change the accounts that steps use.",
      code: "stepAccount.reauthenticate",
      fields: ["account"],
    },
    403,
  );
}

function proofOf(request: Sent): string | null {
  return request.headers.get("X-DDT-Reauthentication");
}

async function confirmItIsMe(label = "Confirm and save"): Promise<void> {
  const proof = await screen.findByRole("dialog", { name: "Confirm it is you" });

  fireEvent.change(within(proof).getByLabelText("Password"), { target: { value: "mine" } });
  press(within(proof).getByRole("button", { name: label }));
}

describe("AccountsPage", () => {
  it("lists the accounts with where they may be used, whether a password is set and what uses them", async () => {
    await open([join, share, backup, lab]);

    await screen.findByRole("grid", { name: "Accounts" });
    const first = within(row("Join account"));

    expect(first.getByText("CORP\\ddt-join")).toBeInTheDocument();
    expect(first.getByText("corp.example")).toBeInTheDocument();
    expect(first.getByText("Set")).toBeInTheDocument();
    expect(first.getByRole("link", { name: "Windows 11 office PCs" })).toHaveAttribute(
      "href",
      "/deployment/sequences/s1",
    );
    expect(first.getByRole("link", { name: "Join the domain" })).toHaveAttribute(
      "href",
      "/deployment/sequences/s1?step=join",
    );

    expect(within(row("Software share")).getByText("files.corp.example")).toBeInTheDocument();
    expect(within(row("Software share")).getByText("Allowed")).toBeInTheDocument();
    expect(within(row("Software share")).getByText("No sequence")).toBeInTheDocument();
    expect(
      within(row("Old backup account")).getByText("Cannot be read, enter it again"),
    ).toBeInTheDocument();
    expect(within(row("Test lab")).getByText("Not set")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("says plainly that an operator can obtain an account a sequence uses, and what to do about it", async () => {
    await open([]);

    expect(await screen.findByText(/^Every operator can obtain an account/)).toHaveTextContent(
      "Every operator can obtain an account a sequence uses by running that sequence on a machine they control. Give each account the least it needs, and prefer an account asked for when the run starts, which is kept encrypted only until the run ends, to one kept here.",
    );
    expect(await screen.findByText("No accounts yet")).toBeInTheDocument();
  });

  it("adds an account after the person confirms their password, and never shows the account's password again", async () => {
    const posts: Sent[] = [];
    const { server } = await open([join], {
      "POST /api/accounts": (request) => {
        posts.push(request);

        if (posts.length === 1) {
          return reauthenticate();
        }

        const body = request.body as SaveAccountRequest;

        return json(
          accountView({
            id: "a5",
            name: body.name,
            userName: body.userName,
            domain: body.domain,
            hosts: body.hosts,
            runAs: body.runAs,
          }),
          201,
        );
      },
    });

    press(await screen.findByRole("button", { name: "Add account" }));
    const drawer = await screen.findByRole("dialog", { name: "Account for steps New account" });
    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Software share");
    fill(within(drawer).getByRole("textbox", { name: "User name" }), "svc-software@corp.example");
    press(within(drawer).getByRole("button", { name: "Add a server" }));
    fill(within(drawer).getByRole("textbox", { name: "Server 1" }), " files.corp.example ");
    press(within(drawer).getByRole("checkbox", { name: /Scripts may run as this account/ }));
    fill(within(drawer).getByLabelText("Password of the account"), secret);
    press(within(drawer).getByRole("button", { name: "Save account" }));

    await confirmItIsMe();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(posts.map(proofOf)).toEqual([null, "proof"]);
    expect(posts.map((request) => request.body)).toEqual([
      expect.objectContaining({ password: { action: "Set", value: secret } }),
      {
        revision: 0,
        name: "Software share",
        userName: "svc-software@corp.example",
        domain: null,
        hosts: ["files.corp.example"],
        runAs: true,
        password: { action: "Set", value: secret },
      },
    ]);
    expect(
      server.requests.find((request) => request.path === "/api/settings/reauthenticate")?.body,
    ).toEqual({ password: "mine", code: null });
    expect(within(row("Software share")).getByText("Set")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(secret);
    expect([...document.querySelectorAll("input")].some((input) => input.value === secret)).toBe(
      false,
    );
  });

  it("keeps a stored password without ever showing a field that holds it", async () => {
    const puts: SaveAccountRequest[] = [];
    await open([share], {
      "PUT /api/accounts/a2": (request) => {
        const body = request.body as SaveAccountRequest;
        puts.push(body);

        return json({ ...share, name: body.name, revision: 2 });
      },
    });

    press(await screen.findByRole("row", { name: /^Software share/ }));
    const drawer = await screen.findByRole("dialog", { name: "Account for steps Software share" });

    expect(drawer.querySelector('input[type="password"]')).toBeNull();
    expect(within(drawer).getByText("Set")).toBeInTheDocument();

    fill(within(drawer).getByRole("textbox", { name: "Name" }), "Software");
    press(within(drawer).getByRole("button", { name: "Save account" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(puts.map((body) => body.password)).toEqual([{ action: "Keep" }]);
  });

  it("clears a stored password on save when asked", async () => {
    const puts: SaveAccountRequest[] = [];
    await open([share], {
      "PUT /api/accounts/a2": (request) => {
        puts.push(request.body as SaveAccountRequest);

        return json({ ...share, password: { isSet: false, unreadable: false, updatedUtc: null } });
      },
    });

    press(await screen.findByRole("row", { name: /^Software share/ }));
    const drawer = await screen.findByRole("dialog", { name: "Account for steps Software share" });
    press(within(drawer).getByRole("button", { name: "Clear" }));
    expect(within(drawer).getByText("Cleared on save")).toBeInTheDocument();
    press(within(drawer).getByRole("button", { name: "Save account" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(puts.map((body) => body.password)).toEqual([{ action: "Clear" }]);
    expect(within(row("Software share")).getByText("Not set")).toBeInTheDocument();
  });

  it("says a new destination needs the password again, and shows the server's refusal at the password", async () => {
    const puts: SaveAccountRequest[] = [];
    await open([share], {
      "PUT /api/accounts/a2": (request) => {
        const body = request.body as SaveAccountRequest;
        puts.push(body);

        return body.password.action === "Keep"
          ? json(
              {
                title: "One or more validation errors occurred.",
                errors: {
                  password: [
                    "Enter the password again: a stored password goes only to the user name, domain and servers it was entered for.",
                  ],
                },
              },
              400,
            )
          : json({ ...share, hosts: body.hosts, revision: 2 });
      },
    });

    press(await screen.findByRole("row", { name: /^Software share/ }));
    const drawer = await screen.findByRole("dialog", { name: "Account for steps Software share" });
    press(within(drawer).getByRole("button", { name: "Add a server" }));
    fill(within(drawer).getByRole("textbox", { name: "Server 2" }), "files-hh.corp.example");

    expect(
      within(drawer).getByText(
        "A new user name, domain or server needs the password again: the stored one goes only where it was entered for.",
      ),
    ).toBeInTheDocument();

    press(within(drawer).getByRole("button", { name: "Save account" }));

    const password = await within(drawer).findByLabelText("New password");
    expect(password).toHaveAccessibleDescription(
      "Enter the password again: a stored password goes only to the user name, domain and servers it was entered for.",
    );

    fill(password, secret);
    press(within(drawer).getByRole("button", { name: "Save account" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(puts.map((body) => body.password)).toEqual([
      { action: "Keep" },
      { action: "Set", value: secret },
    ]);
    expect(puts[1]?.hosts).toEqual(["files.corp.example", "files-hh.corp.example"]);
  });

  it("names the sequences that use an account and offers no deletion until they choose another", async () => {
    await open([join]);

    await screen.findByRole("grid", { name: "Accounts" });
    await chooseMenuItem(
      within(row("Join account")).getByRole("button", { name: "Actions for Join account" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Join account?" });

    expect(dialog).toHaveTextContent(
      "The sequence Windows 11 office PCs uses this account. Choose another account there first.",
    );
    expect(within(dialog).getByRole("button", { name: "Delete account" })).toBeDisabled();
  });

  it("deletes an account no sequence uses once the person confirmed their password", async () => {
    const deletes: Sent[] = [];
    await open([join, share], {
      "DELETE /api/accounts/a2": (request) => {
        deletes.push(request);

        return deletes.length === 1 ? reauthenticate() : json(null, 204);
      },
    });

    await screen.findByRole("grid", { name: "Accounts" });
    await chooseMenuItem(
      within(row("Software share")).getByRole("button", { name: "Actions for Software share" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Software share?" });
    press(within(dialog).getByRole("button", { name: "Delete account" }));

    await confirmItIsMe("Confirm and delete");

    await waitFor(() => {
      expect(
        within(table()).queryByRole("row", { name: /^Software share/, hidden: true }),
      ).toBeNull();
    });
    // The first delete carried the proof the page already held, and the server didn't accept it anymore.
    expect(deletes.map(proofOf)[1]).toBe("proof");
  });

  it("shows the server's refusal of a deletion when a sequence came to use the account", async () => {
    await open([share], {
      "DELETE /api/accounts/a2": () =>
        json(
          {
            title: "The sequence Kiosk uses this account. Choose another account there first.",
            code: "stepAccount.inUse",
            args: { count: 1, sequences: "Kiosk" },
          },
          409,
        ),
    });

    await screen.findByRole("grid", { name: "Accounts" });
    await chooseMenuItem(
      within(row("Software share")).getByRole("button", { name: "Actions for Software share" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Software share?" });
    press(within(dialog).getByRole("button", { name: "Delete account" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "The sequence Kiosk uses this account. Choose another account there first.",
    );
  });

  it("shows a viewer the accounts without a way to change them", async () => {
    await open([join, share], {}, viewer);

    await screen.findByRole("grid", { name: "Accounts" });
    expect(screen.queryByRole("button", { name: "Add account" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();

    press(row("Software share"));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("takes accounts changed and removed elsewhere from the hub", async () => {
    const { server, hub } = await open([join, share]);

    await screen.findByRole("grid", { name: "Accounts" });
    act(() => {
      hub?.push("accountChanged", { ...share, runAs: false, revision: 2 });
      hub?.push("accountChanged", lab);
    });

    expect(await within(row("Test lab")).findByText("Not set")).toBeInTheDocument();
    expect(row("Test lab")).toHaveClass("live-new");
    expect(row("Software share")).toHaveClass("live-flash");
    expect(within(row("Software share")).getByText("No")).toBeInTheDocument();

    act(() => {
      hub?.push("accountsRemoved", { accountIds: ["a1"] });
    });

    await waitFor(() => {
      expect(
        within(table()).queryByRole("row", { name: /^Join account/, hidden: true }),
      ).toBeNull();
    });
    expect(server.count("GET /api/accounts")).toBe(1);
  });

  it("has no violations with an account's drawer open", async () => {
    await open([share]);

    press(await screen.findByRole("row", { name: /^Software share/ }));
    await screen.findByRole("dialog", { name: "Account for steps Software share" });
    await expectNoAxeViolations();
  });
});
