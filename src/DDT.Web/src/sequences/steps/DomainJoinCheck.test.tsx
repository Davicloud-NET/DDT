// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { DomainJoinCheckView } from "@/deployments/deployments";

import { DomainJoinCheck } from "./DomainJoinCheck";

const canJoin: DomainJoinCheckView = {
  canJoin: true,
  domain: "corp.example",
  userName: "CORP\\ddt-join",
  controller: "dc1.corp.example",
  container: "CN=Computers,DC=corp,DC=example",
  findings: [
    { level: "Passed", text: "Signed in to dc1.corp.example as CORP\\ddt-join over LDAPS." },
    { level: "Warning", text: "It joins within the quota: 3 of 10 used, 7 left." },
  ],
  checkedUtc: "2026-09-24T12:00:00Z",
};

// Answers the check with the given status and body, and records what each check asked for.
function serve(status: number, body: unknown) {
  const asked: unknown[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = (input instanceof Request ? input.url : input.toString()).replace(
        "http://localhost",
        "",
      );

      if (path === "/api/auth/session") {
        return Promise.resolve(new Response(null, { headers: { "X-CSRF-TOKEN": "token" } }));
      }

      if (path === "/api/deployments/domain-check" && init?.method === "POST") {
        asked.push(JSON.parse(typeof init.body === "string" ? init.body : "null"));

        return Promise.resolve(new Response(JSON.stringify(body), { status }));
      }

      return Promise.resolve(new Response(null, { status: 404 }));
    }),
  );

  return asked;
}

function renderCheck(organizationalUnit: string | null) {
  const queryClient = new QueryClient();
  const element = (unit: string | null) => (
    <QueryClientProvider client={queryClient}>
      <DomainJoinCheck organizationalUnit={unit} />
    </QueryClientProvider>
  );
  const view = render(element(organizationalUnit));

  return {
    changeUnit: (unit: string | null) => {
      view.rerender(element(unit));
    },
  };
}

describe("DomainJoinCheck", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("asks for the step's organizational unit and lists what the domain said", async () => {
    const asked = serve(200, canJoin);
    renderCheck("OU=Kiosks,DC=corp,DC=example");

    fireEvent.click(screen.getByRole("button", { name: "Check the join account" }));

    expect(await screen.findByRole("status")).toHaveTextContent("It can join machines here.");
    const findings = within(
      screen.getByRole("list", { name: "What the check found" }),
    ).getAllByRole("listitem");
    expect(findings.map((item) => item.textContent)).toEqual([
      "OK Signed in to dc1.corp.example as CORP\\ddt-join over LDAPS.",
      "Warning It joins within the quota: 3 of 10 used, 7 left.",
    ]);
    expect(asked).toEqual([{ organizationalUnit: "OU=Kiosks,DC=corp,DC=example" }]);
  });

  it("says when the account cannot join", async () => {
    serve(200, {
      ...canJoin,
      canJoin: false,
      findings: [{ level: "Problem", text: "dc1.corp.example did not accept the password." }],
    });
    renderCheck(null);

    fireEvent.click(screen.getByRole("button", { name: "Check the join account" }));

    expect(await screen.findByRole("status")).toHaveTextContent("It cannot join machines here.");
    expect(screen.getByText(/did not accept the password/)).toBeInTheDocument();
  });

  it("tells someone who is no administrator that only administrators may check", async () => {
    serve(403, {});
    renderCheck(null);

    fireEvent.click(screen.getByRole("button", { name: "Check the join account" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Only administrators may check the join account.",
    );
  });

  it("says a result is for another organizational unit once the step's changed", async () => {
    serve(200, canJoin);
    const { changeUnit } = renderCheck(null);

    fireEvent.click(screen.getByRole("button", { name: "Check the join account" }));
    expect(await screen.findByRole("status")).not.toHaveTextContent("changed since");
    changeUnit("OU=Other,DC=corp,DC=example");

    expect(screen.getByRole("status")).toHaveTextContent(
      "It can join machines here. The organizational unit changed since, check again.",
    );
  });
});
