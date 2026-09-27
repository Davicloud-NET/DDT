// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { CurrentUser } from "@/auth/auth";
import { fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { administrator } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import type { Routes } from "@/test/server";

function open(routes: Routes = {}, user: CurrentUser = administrator) {
  return renderPage({ path: "/account", user, routes });
}

function field(label: string): HTMLElement {
  return screen.getByLabelText(label);
}

function fillPasswords(current: string, next: string, again: string) {
  fill(field("Current password"), current);
  fill(field("New password"), next);
  fill(field("New password again"), again);
}

function authenticator(): HTMLElement {
  const panel = screen.getByRole("heading", { name: "Authenticator" }).closest("section");

  if (panel === null) {
    throw new Error("There is no authenticator panel.");
  }

  return panel;
}

const enrollment: Routes = {
  "POST /api/auth/2fa/enroll": {
    body: { sharedKey: "ABCDEFGHIJKL", authenticatorUri: "otpauth://totp/DDT:admin?secret=X" },
  },
  "POST /api/auth/2fa/enable": { body: { codes: ["aaaa-1111", "bbbb-2222"] } },
};

describe("AccountPage", () => {
  it("says who is signed in, from where and in which role", async () => {
    await open({}, { ...administrator, displayName: "Ada Admin" });

    expect(
      await screen.findByRole("heading", { level: 1, name: "Account and security" }),
    ).toBeInTheDocument();
    const facts = Object.fromEntries(
      screen
        .getAllByRole("term")
        .map((term) => [term.textContent, term.nextElementSibling?.textContent]),
    );
    expect(facts).toEqual({
      "User name": "administrator",
      Name: "Ada Admin",
      Account: "Local account",
      Role: "Administrator",
    });
  });

  it("changes the password", async () => {
    const { server } = await open({ "POST /api/auth/password": { status: 204 } });

    await screen.findByRole("heading", { name: "Password" });
    fillPasswords("printed once 12345", "a longer secret 42", "a longer secret 42");
    press(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByText("Password changed.")).toBeInTheDocument();
    expect(server.changes().map((request) => [request.path, request.body])).toEqual([
      [
        "/api/auth/password",
        { currentPassword: "printed once 12345", newPassword: "a longer secret 42" },
      ],
    ]);
    // The fields are emptied, so the password does not stay on the page.
    expect(field("New password")).toHaveValue("");
  });

  it("does not send a new password that was typed differently twice", async () => {
    const { server } = await open();

    await screen.findByRole("heading", { name: "Password" });
    fillPasswords("printed once 12345", "a longer secret 42", "a longer secret 43");
    press(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() => {
      expect(field("New password again")).toHaveAttribute("aria-invalid", "true");
    });
    expect(field("New password again")).toHaveAccessibleDescription(
      "The new passwords do not match.",
    );
    expect(server.changes()).toEqual([]);
  });

  it("shows what the server refuses", async () => {
    await open({
      "POST /api/auth/password": {
        status: 400,
        body: {
          errors: { PasswordRequiresDigit: ["Passwords must have at least one digit ('0'-'9')."] },
        },
      },
    });

    await screen.findByRole("heading", { name: "Password" });
    fillPasswords("printed once 12345", "no digits in here", "no digits in here");
    press(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Passwords must have at least one digit ('0'-'9').",
    );
  });

  it("sends directory accounts to the directory for their password", async () => {
    await open({}, { ...administrator, source: "Directory" });

    expect(
      await screen.findByText("This account comes from the directory. Change its password there."),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Current password")).not.toBeInTheDocument();
  });

  it("says that single sign-on accounts have no password in DDT", async () => {
    await open({}, { ...administrator, source: "External" });

    expect(
      await screen.findByText(
        "This account signs in through single sign-on and has no password in DDT.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Current password")).not.toBeInTheDocument();
  });

  it("sets up an authenticator and shows the recovery codes once", async () => {
    const { server } = await open(enrollment);

    press(await screen.findByRole("button", { name: "Set up an authenticator" }));

    expect(await screen.findByText("ABCD EFGH IJKL")).toBeInTheDocument();
    expect(
      screen.getByRole("img", { name: "QR code for your authenticator app" }),
    ).toBeInTheDocument();
    const turnOn = screen.getByRole("button", { name: "Turn on" });
    expect(turnOn).toBeDisabled();

    fill(screen.getByRole("textbox", { name: "Code from the authenticator app" }), " 123456 ");
    press(turnOn);

    const dialog = await screen.findByRole("dialog", { name: "Your recovery codes" });
    expect(within(dialog).getByText("aaaa-1111")).toBeInTheDocument();
    expect(within(dialog).getByText("bbbb-2222")).toBeInTheDocument();
    expect(server.changes().map((request) => [request.path, request.body])).toEqual([
      ["/api/auth/2fa/enroll", null],
      ["/api/auth/2fa/enable", { code: "123456" }],
    ]);

    press(within(dialog).getByRole("button", { name: "I have saved them" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    // The signed-in user is patched, not read again.
    expect(within(authenticator()).getByText("On")).toBeInTheDocument();
    expect(screen.queryByText("aaaa-1111")).not.toBeInTheDocument();
    expect(server.count("GET /api/auth/me")).toBe(1);
  });

  it("drops a set-up that is cancelled", async () => {
    const { server } = await open(enrollment);

    press(await screen.findByRole("button", { name: "Set up an authenticator" }));
    await screen.findByText("ABCD EFGH IJKL");
    press(screen.getByRole("button", { name: "Cancel" }));

    expect(
      await screen.findByRole("button", { name: "Set up an authenticator" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("ABCD EFGH IJKL")).not.toBeInTheDocument();
    expect(server.changes().map((request) => request.path)).toEqual(["/api/auth/2fa/enroll"]);
  });

  it("shows why the server refused the code", async () => {
    await open({
      ...enrollment,
      "POST /api/auth/2fa/enable": {
        status: 400,
        body: { title: "The code is not valid. Enter the current code from the app." },
      },
    });

    press(await screen.findByRole("button", { name: "Set up an authenticator" }));
    fill(await screen.findByRole("textbox", { name: "Code from the authenticator app" }), "000000");
    press(screen.getByRole("button", { name: "Turn on" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The code is not valid. Enter the current code from the app.",
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("turns the authenticator off with a current code", async () => {
    const { server } = await open(
      { "POST /api/auth/2fa/disable": { status: 204 } },
      { ...administrator, twoFactorEnabled: true },
    );

    const turnOff = await screen.findByRole("button", { name: "Turn off" });
    expect(within(authenticator()).getByText("On")).toBeInTheDocument();
    expect(turnOff).toBeDisabled();

    fill(screen.getByRole("textbox", { name: "Code from the authenticator app" }), "654321");
    press(turnOff);

    expect(await within(authenticator()).findByText("Off")).toBeInTheDocument();
    expect(server.changes().map((request) => [request.path, request.body])).toEqual([
      ["/api/auth/2fa/disable", { code: "654321" }],
    ]);
    expect(screen.getByRole("button", { name: "Set up an authenticator" })).toBeInTheDocument();
  });

  it("makes new recovery codes", async () => {
    await open(
      { "POST /api/auth/2fa/recovery-codes": { body: { codes: ["cccc-3333"] } } },
      { ...administrator, twoFactorEnabled: true },
    );

    press(await screen.findByRole("button", { name: "Make new recovery codes" }));

    const dialog = await screen.findByRole("dialog", { name: "Your recovery codes" });
    expect(within(dialog).getByText("cccc-3333")).toBeInTheDocument();
  });

  describe("accessibility", () => {
    it("has no violations for a local account", async () => {
      await open();

      await screen.findByRole("button", { name: "Set up an authenticator" });
      await expectNoAxeViolations();
    });

    it("has no violations while an authenticator is set up", async () => {
      await open(enrollment);

      press(await screen.findByRole("button", { name: "Set up an authenticator" }));
      await screen.findByText("ABCD EFGH IJKL");
      await expectNoAxeViolations();
    });

    it("has no violations with the recovery codes shown", async () => {
      await open(
        { "POST /api/auth/2fa/recovery-codes": { body: { codes: ["cccc-3333", "dddd-4444"] } } },
        { ...administrator, twoFactorEnabled: true },
      );

      press(await screen.findByRole("button", { name: "Make new recovery codes" }));
      await screen.findByRole("dialog");
      await expectNoAxeViolations();
    });
  });
});
