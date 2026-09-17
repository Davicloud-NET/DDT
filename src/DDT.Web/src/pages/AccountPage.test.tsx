import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CurrentUser } from "@/auth/auth";

import { AccountPage } from "./AccountPage";

const user: CurrentUser = {
  id: "0193a4b2-0000-7000-8000-000000000001",
  userName: "admin",
  displayName: null,
  source: "Local",
  twoFactorEnabled: false,
  roles: ["Administrator"],
};

function renderWith(
  answers: Record<string, { status?: number; body?: unknown }>,
  currentUser: CurrentUser = user,
) {
  const calls: { url: string; body: unknown }[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      const path = url.replace("http://localhost", "");

      if (path === "/api/auth/me") {
        return Promise.resolve(new Response(JSON.stringify(currentUser), { status: 200 }));
      }

      calls.push({
        url: path,
        body: typeof init?.body === "string" ? JSON.parse(init.body) : null,
      });

      const answer = answers[path] ?? { status: 200, body: null };

      return Promise.resolve(
        new Response(answer.body === undefined ? null : JSON.stringify(answer.body), {
          status: answer.status ?? 200,
        }),
      );
    }),
  );

  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <AccountPage />
    </QueryClientProvider>,
  );

  return calls;
}

function type(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe("AccountPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("changes the password", async () => {
    const calls = renderWith({ "/api/auth/password": { status: 200, body: null } });

    type("Current password", "printed once 12345");
    type("New password", "a longer secret 42");
    type("New password again", "a longer secret 42");
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByText("Password changed.")).toBeInTheDocument();
    expect(calls).toContainEqual({
      url: "/api/auth/password",
      body: { currentPassword: "printed once 12345", newPassword: "a longer secret 42" },
    });
  });

  it("does not send a new password that was typed differently twice", async () => {
    const calls = renderWith({});

    type("Current password", "printed once 12345");
    type("New password", "a longer secret 42");
    type("New password again", "a longer secret 43");
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The new passwords do not match.");
    expect(calls.some((call) => call.url === "/api/auth/password")).toBe(false);
  });

  it("shows what the server refuses", async () => {
    renderWith({
      "/api/auth/password": {
        status: 400,
        body: { errors: { PasswordTooShort: ["Passwords must be at least 12 characters."] } },
      },
    });

    type("Current password", "printed once 12345");
    type("New password", "short");
    type("New password again", "short");
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Passwords must be at least 12 characters.",
    );
  });

  it("sets up an authenticator and shows the recovery codes once", async () => {
    renderWith({
      "/api/auth/2fa/enroll": {
        body: {
          sharedKey: "ABCD EFGH IJKL",
          authenticatorUri: "otpauth://totp/DDT:admin?secret=X",
        },
      },
      "/api/auth/2fa/enable": { body: { codes: ["aaaa-1111", "bbbb-2222"] } },
    });

    fireEvent.click(await screen.findByRole("button", { name: "Set up an authenticator" }));

    expect(await screen.findByText("ABCD EFGH IJKL")).toBeInTheDocument();

    type("Authenticator code", "123456");
    fireEvent.click(screen.getByRole("button", { name: "Turn on" }));

    expect(await screen.findByText("aaaa-1111")).toBeInTheDocument();
    expect(screen.getByText("bbbb-2222")).toBeInTheDocument();
  });

  it("sends directory accounts to the directory for their password", async () => {
    renderWith({}, { ...user, source: "Directory" });

    expect(
      await screen.findByText("This account comes from the directory. Change its password there."),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Current password")).not.toBeInTheDocument();
  });
});
