// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import { fill, press, selectKey } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { testHub } from "@/test/fakeHub";
import {
  administrator,
  json,
  operator,
  reads,
  servePage,
  stubClipboard,
  type Handler,
  type Sent,
} from "@/test/serve";
import type { DirectoryView } from "@/users/users";

import type { SettingsSectionUpdate, SettingsSectionView } from "./settings";
import type { LdapSettings, OidcSettings } from "./signIn";
import { SignInSettingsPage } from "./SignInSettingsPage";

const ADMINS = "CN=DDT Admins,OU=Groups,DC=corp,DC=example";
const OPERATORS = "CN=DDT Operators,OU=Groups,DC=corp,DC=example";

const ldapValues: LdapSettings = {
  enabled: true,
  host: "dc01.corp.example",
  port: 636,
  transport: "Ldaps",
  baseDn: "DC=corp,DC=example",
  bindDn: "CN=ddt-bind,OU=Service accounts,DC=corp,DC=example",
  userFilter: "(&(objectClass=user)(sAMAccountName={0}))",
  immutableIdAttribute: "objectGUID",
  displayNameAttribute: "displayName",
  emailAttribute: "mail",
  resolveNestedGroups: true,
  groupRoleMap: { [OPERATORS]: "Operator" },
  timeout: "00:00:10",
};

const oidcValues: OidcSettings = {
  enabled: false,
  authority: "",
  clientId: "",
  displayName: "Single sign on",
  scopes: ["openid", "profile", "email"],
  autoProvision: false,
  autoProvisionRole: "Viewer",
  groupsClaim: "groups",
  groupRoleMap: {},
};

function view<T>(
  section: string,
  values: T,
  overrides: Partial<SettingsSectionView<T>> = {},
): SettingsSectionView<T> {
  return {
    section,
    version: 3,
    updatedUtc: new Date(Date.now() - 2 * 3_600_000).toISOString(),
    updatedBy: "admin",
    values,
    secrets: {},
    locked: [],
    problems: [],
    warnings: [],
    apply: null,
    reauthenticate: [],
    ...overrides,
  };
}

function ldap(overrides: Partial<SettingsSectionView<LdapSettings>> = {}) {
  return view("ldap", ldapValues, {
    secrets: {
      bindPassword: { isSet: true, unreadable: false, updatedUtc: "2026-09-20T08:00:00Z" },
    },
    reauthenticate: ["host", "port", "transport", "groupRoleMap"],
    ...overrides,
  });
}

function oidc(overrides: Partial<SettingsSectionView<OidcSettings>> = {}) {
  return view("oidc", oidcValues, {
    updatedUtc: null,
    updatedBy: null,
    secrets: { clientSecret: { isSet: false, unreadable: false, updatedUtc: null } },
    reauthenticate: [
      "authority",
      "autoProvision",
      "autoProvisionRole",
      "groupsClaim",
      "groupRoleMap",
    ],
    ...overrides,
  });
}

const directory: DirectoryView = {
  enabled: true,
  host: "dc01.corp.example",
  baseDn: "DC=corp,DC=example",
  groupRoleMap: [{ group: OPERATORS, name: "DDT Operators", role: "Operator" }],
};

// An administrator who signs in through the directory. Saving the connection or the map needs a test of their own
// sign-in first.
const directoryAdministrator: CurrentUser = {
  ...administrator,
  userName: "j.admin",
  displayName: "Jana Admin",
  source: "Directory",
};

function serve(handlers: Record<string, Handler>, user: CurrentUser = administrator) {
  return servePage({
    user,
    path: "/admin/sign-in",
    component: () => (
      <main>
        <SignInSettingsPage />
      </main>
    ),
    handlers: {
      "GET /api/settings/ldap": () => json(ldap()),
      "GET /api/settings/oidc": () => json(oidc()),
      "GET /api/directory": () => json(directory),
      ...handlers,
    },
  });
}

function puts<T>(requests: readonly Sent[], section: string) {
  return requests
    .filter((request) => request.method === "PUT" && request.path === `/api/settings/${section}`)
    .map((request) => ({
      update: request.body as SettingsSectionUpdate<T>,
      headers: request.headers,
    }));
}

// Picks the role a map entry gives. An option's name is the role plus what the role may do.
async function chooseRole(map: HTMLElement, key: string, role: string): Promise<void> {
  press(selectKey(map, `Role that ${key} gives`));
  const listbox = await screen.findByRole("listbox");

  press(within(listbox).getByRole("option", { name: new RegExp(`^${role}`) }));
  await waitFor(() => {
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });
}

// A section's panel, found by its title.
function panel(title: string): HTMLElement {
  const section = screen.getByRole("heading", { name: title, level: 2 }).closest("section");

  if (section === null) {
    throw new Error(`There is no panel ${title}.`);
  }

  return section;
}

// Answers a save with the section, holding the values the save sent.
function savedAs<T>(
  section: (overrides: Partial<SettingsSectionView<T>>) => SettingsSectionView<T>,
) {
  return (request: Sent) =>
    json(
      section({
        version: 4,
        updatedUtc: new Date().toISOString(),
        updatedBy: "Ada Admin",
        values: (request.body as SettingsSectionUpdate<T>).values,
      }),
    );
}

describe("SignInSettingsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("tells anyone but an administrator that only administrators change sign-in, and asks nothing", async () => {
    const { requests } = serve({}, operator);

    expect(
      await screen.findByText(/^Only administrators change how people sign in\./),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(
      requests.filter(
        (request) =>
          request.path.startsWith("/api/settings") || request.path.startsWith("/api/directory"),
      ),
    ).toEqual([]);
  });

  it("adds a group found by name, gives it a role, and saves the map without reading it again", async () => {
    const { requests } = serve({
      "GET /api/directory/groups?query=ddt&limit=20": () =>
        json([
          { distinguishedName: ADMINS, name: "DDT Admins", description: "Deployment admins" },
          { distinguishedName: OPERATORS, name: "DDT Operators", description: null },
        ]),
      "PUT /api/settings/ldap": savedAs(ldap),
    });

    const map = await screen.findByRole("list", { name: "Groups and the roles they give" });
    await waitFor(() => {
      expect(map).toHaveTextContent(`DDT Operators${OPERATORS}`);
    });

    vi.useFakeTimers({ shouldAdvanceTime: true });
    fireEvent.change(screen.getByRole("searchbox", { name: "Find a directory group" }), {
      target: { value: "ddt" },
    });
    await act(() => vi.advanceTimersByTimeAsync(400));

    const found = await screen.findByRole("list", { name: "Directory groups found" });
    expect(found).toHaveTextContent("DDT OperatorsCN=DDT Operators");
    expect(found).toHaveTextContent("Gives Operator");
    expect(
      within(found).queryByRole("button", { name: "Add DDT Operators to the map" }),
    ).not.toBeInTheDocument();

    press(within(found).getByRole("button", { name: "Add DDT Admins to the map" }));
    await waitFor(() => {
      expect(map).toHaveTextContent(`DDT Admins${ADMINS}`);
    });
    expect(found).toHaveTextContent("Gives Viewer");
    await expectNoAxeViolations();

    await chooseRole(map, ADMINS, "Administrator");
    press(within(panel("Directory")).getByRole("button", { name: "Save" }));

    expect(
      await within(panel("Directory")).findByText("Saved now by Ada Admin."),
    ).toBeInTheDocument();
    const sent = puts<LdapSettings>(requests, "ldap");
    expect(sent.map((one) => one.update.values.groupRoleMap)).toEqual([
      { [OPERATORS]: "Operator", [ADMINS]: "Administrator" },
    ]);
    expect(sent[0]?.update.version).toBe(3);
    expect(sent[0]?.headers["x-ddt-directory-proof"]).toBeUndefined();
    expect(reads(requests, "/api/settings/ldap")).toBe(1);
  });

  it("tests the form's values, and sends the proof of a directory administrator's own sign-in with the save", async () => {
    const { requests } = serve(
      {
        "POST /api/settings/ldap/test": () =>
          json({
            bound: true,
            userFound: true,
            passwordAccepted: true,
            groups: [ADMINS, "CN=Staff,OU=Groups,DC=corp,DC=example"],
            role: "Administrator",
            message:
              "The bind as CN=ddt-bind to dc02.corp.example succeeded. The group map makes the account Administrator.",
            proof: "proof-of-j.admin",
          }),
        "PUT /api/settings/ldap": savedAs(ldap),
      },
      directoryAdministrator,
    );

    const directorySection = await waitFor(() => panel("Directory"));
    const host = await within(directorySection).findByRole("textbox", { name: "Directory server" });
    fill(host, "dc02.corp.example");

    expect(
      within(directorySection).getByText(/^You sign in through the directory, and these changes/),
    ).toBeInTheDocument();

    const userName = within(directorySection).getByRole("textbox", { name: "User name, optional" });
    expect(userName).toHaveValue("j.admin");
    fill(within(directorySection).getByLabelText("Password, optional"), "correct horse");
    press(within(directorySection).getByRole("button", { name: "Test the directory" }));

    expect(
      await within(directorySection).findByText(
        "Your sign-in with these values keeps you an administrator. Save them within 5 minutes.",
      ),
    ).toBeInTheDocument();
    expect(directorySection).toHaveTextContent("Bind accountSigned in");
    expect(directorySection).toHaveTextContent("UserFound");
    expect(directorySection).toHaveTextContent("PasswordAccepted");
    expect(directorySection).toHaveTextContent("Role at sign-inAdministrator");
    expect(directorySection).toHaveTextContent("The group map makes the account Administrator.");
    expect(within(directorySection).getByLabelText("Password, optional")).toHaveValue("");
    expect(
      within(directorySection).queryByText(/^You sign in through the directory, and these changes/),
    ).not.toBeInTheDocument();

    await expectNoAxeViolations();

    const test = requests.find((request) => request.path === "/api/settings/ldap/test");
    expect(test?.body).toEqual({
      values: { ...ldapValues, host: "dc02.corp.example" },
      secrets: {},
      userName: "j.admin",
      password: "correct horse",
    });

    press(within(directorySection).getByRole("button", { name: "Save" }));
    expect(
      await within(directorySection).findByText("Saved now by Ada Admin."),
    ).toBeInTheDocument();
    expect(
      puts<LdapSettings>(requests, "ldap").map((one) => one.headers["x-ddt-directory-proof"]),
    ).toEqual(["proof-of-j.admin"]);
  });

  it("asks for a new test once the values change after it, and shows the server's refusal of a save without one", async () => {
    const refusal =
      "You sign in through the directory, and these values decide whether you still can. Test your own sign-in with them first; the save is accepted while a test that kept you an administrator is less than 5 minutes old.";
    const { requests } = serve(
      {
        "POST /api/settings/ldap/test": () =>
          json({
            bound: true,
            userFound: true,
            passwordAccepted: false,
            groups: [],
            role: null,
            message: "The directory refused the password of j.admin.",
            proof: null,
          }),
        "PUT /api/settings/ldap": () =>
          json(
            {
              title: "One or more validation errors occurred.",
              status: 400,
              errors: { "": [refusal] },
            },
            400,
          ),
      },
      directoryAdministrator,
    );

    const directorySection = await waitFor(() => panel("Directory"));
    fill(
      await within(directorySection).findByRole("textbox", { name: "Search base" }),
      "OU=Staff,DC=corp,DC=example",
    );
    fill(within(directorySection).getByLabelText("Password, optional"), "wrong");
    press(within(directorySection).getByRole("button", { name: "Test the directory" }));

    expect(
      await within(directorySection).findByText("The directory refused the password of j.admin."),
    ).toBeInTheDocument();
    expect(directorySection).toHaveTextContent("PasswordRefused");
    expect(
      within(directorySection).getByText(/^You sign in through the directory, and these changes/),
    ).toBeInTheDocument();

    press(within(directorySection).getByRole("button", { name: "Save" }));

    expect(await within(directorySection).findByText(refusal)).toBeInTheDocument();
    expect(within(directorySection).getByText("Nothing was saved.")).toBeInTheDocument();
    expect(
      puts<LdapSettings>(requests, "ldap").map((one) => one.headers["x-ddt-directory-proof"]),
    ).toEqual([undefined]);
  });

  it("puts a refused entry of the map on its own row", async () => {
    const typo = "CN=DDT Admns,OU=Groups,DC=corp,DC=example";
    serve({
      "PUT /api/settings/ldap": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            status: 400,
            errors: {
              [`groupRoleMap[${typo}]`]: [
                "'Superuser' is not a DDT role. Use Administrator, Operator, Viewer.",
              ],
            },
          },
          400,
        ),
    });

    const map = await screen.findByRole("list", { name: "Groups and the roles they give" });
    fill(
      within(panel("Directory")).getByRole("textbox", {
        name: "Add a group by its distinguished name",
      }),
      ` ${typo} `,
    );
    press(within(panel("Directory")).getByRole("button", { name: "Add group" }));
    await waitFor(() => {
      expect(within(map).getAllByRole("listitem")).toHaveLength(2);
    });

    press(within(panel("Directory")).getByRole("button", { name: "Save" }));

    const message = await within(map).findByText(
      "'Superuser' is not a DDT role. Use Administrator, Operator, Viewer.",
    );
    const rows = within(map).getAllByRole("listitem");
    expect(rows[1]).toContainElement(message);
    expect(rows[1]).toHaveTextContent(typo);
    expect(rows[0]).not.toHaveTextContent("Superuser");
  });

  it("shows the redirect URI to register, tests the provider and saves single sign-on", async () => {
    const clipboard = stubClipboard();
    const uri = `${window.location.origin}/api/auth/external/callback`;
    const { requests } = serve({
      "POST /api/settings/oidc/test": () =>
        json({
          reached: true,
          issuer: "https://login.example.com/realms/ddt",
          redirectUri: uri,
          message: `The provider answered as https://login.example.com/realms/ddt. Register ${uri} as the redirect URI of DDT's client there.`,
        }),
      "POST /api/settings/reauthenticate": () =>
        json({ token: "fresh", expiresUtc: new Date(Date.now() + 300_000).toISOString() }),
      "PUT /api/settings/oidc": (request) =>
        request.headers["x-ddt-reauthentication"] === "fresh"
          ? savedAs(oidc)(request)
          : json(
              {
                title: "Enter your password again to change authority.",
                status: 403,
                fields: ["authority"],
              },
              403,
            ),
    });

    const sso = await waitFor(() => panel("Single sign-on"));
    expect(within(sso).getByText(uri)).toBeInTheDocument();
    press(within(sso).getByRole("button", { name: "Copy" }));
    await waitFor(() => {
      expect(clipboard.written).toEqual([uri]);
    });

    press(within(sso).getByRole("switch", { name: "Offer single sign-on on the sign-in page" }));
    fill(
      within(sso).getByRole("textbox", { name: "Provider address" }),
      "https://login.example.com/realms/ddt",
    );
    fill(within(sso).getByRole("textbox", { name: "Client ID" }), "ddt");
    press(within(sso).getByRole("button", { name: "Set" }));
    fill(within(sso).getByLabelText("New value"), "s3cret");

    press(within(sso).getByRole("button", { name: "Test the provider" }));
    expect(
      await within(sso).findByText("https://login.example.com/realms/ddt"),
    ).toBeInTheDocument();
    expect(sso).toHaveTextContent("ProviderReached");
    expect(requests.find((request) => request.path === "/api/settings/oidc/test")?.body).toEqual({
      authority: "https://login.example.com/realms/ddt",
    });

    fill(within(sso).getByRole("textbox", { name: "Add a claim value" }), "ddt-admins");
    press(within(sso).getByRole("button", { name: "Add value" }));
    const map = await within(sso).findByRole("list", {
      name: "Claim values and the roles they give",
    });
    await chooseRole(map, "ddt-admins", "Administrator");

    press(within(sso).getByRole("button", { name: "Save" }));
    const proof = await screen.findByRole("dialog", { name: "Confirm it is you" });
    fill(within(proof).getByLabelText("Password"), "mine");
    press(within(proof).getByRole("button", { name: "Confirm and save" }));

    expect(await within(sso).findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    const sent = puts<OidcSettings>(requests, "oidc");
    expect(sent).toHaveLength(2);
    expect(sent[1]?.update).toEqual({
      version: 3,
      values: {
        ...oidcValues,
        enabled: true,
        authority: "https://login.example.com/realms/ddt",
        clientId: "ddt",
        groupRoleMap: { "ddt-admins": "Administrator" },
      },
      secrets: { clientSecret: { action: "Set", value: "s3cret" } },
      confirm: [],
    });
    expect(reads(requests, "/api/settings/oidc")).toBe(1);
  });

  it("takes a directory change saved elsewhere, and reads the directory's group names again", async () => {
    const { requests, queryClient } = serve({});
    const hub = testHub(queryClient);

    await screen.findByRole("list", { name: "Groups and the roles they give" });
    hub.live.start();
    await waitFor(() => {
      expect(reads(requests, "/api/directory")).toBe(2);
    });
    const ldapReads = reads(requests, "/api/settings/ldap");

    act(() => {
      hub.push(
        "settingsChanged",
        ldap({
          version: 5,
          updatedBy: "b.bauer",
          updatedUtc: new Date().toISOString(),
          values: {
            ...ldapValues,
            groupRoleMap: { [OPERATORS]: "Operator", [ADMINS]: "Administrator" },
          },
        }),
      );
    });

    expect(
      await within(panel("Directory")).findByText("Saved now by b.bauer."),
    ).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Groups and the roles they give" })).toHaveTextContent(
      ADMINS,
    );
    await waitFor(() => {
      expect(reads(requests, "/api/directory")).toBe(3);
    });
    expect(reads(requests, "/api/settings/ldap")).toBe(ldapReads);
    hub.live.stop();
  });

  it("says what the unencrypted transport does, and has no accessibility violations", async () => {
    serve({
      "GET /api/settings/ldap": () =>
        json(ldap({ values: { ...ldapValues, transport: "UnencryptedDangerous", port: 389 } })),
    });

    expect(
      await screen.findByText("Passwords cross the network in clear text"),
    ).toBeInTheDocument();
    await screen.findByText("Never changed on this page.");
    await expectNoAxeViolations();
  });
});
