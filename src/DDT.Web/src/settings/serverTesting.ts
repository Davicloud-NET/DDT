// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { expect, onTestFinished } from "vitest";

import { expectNoAxeViolations } from "@/test/axe";

import type { CurrentUser } from "@/auth/auth";
import { administrator, json, servePage, type Handler, type Sent } from "@/test/serve";

import type { AgentBinaryView } from "./agentBinary";
import type { CertificateSettings, CertificateView } from "./certificate";
import { ServerPage } from "./ServerPage";
import type { ServerTab } from "./serverSearch";
import type { SettingsOverview, SettingsSectionView } from "./settings";

// The fake server of the Server page's tests: the overview, a section view, the certificate and the agent as the
// settings API answers them.

export function hoursAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

export function overview(overrides: Partial<SettingsOverview> = {}): SettingsOverview {
  return {
    sections: [
      {
        section: "deployment",
        kind: "Live",
        version: 4,
        updatedUtc: hoursAgo(2),
        updatedBy: "admin",
        lockedCount: 1,
        problemCount: 0,
        apply: null,
      },
      {
        section: "pxe",
        kind: "Restart",
        version: 7,
        updatedUtc: null,
        updatedBy: null,
        lockedCount: 0,
        problemCount: 2,
        apply: [
          {
            host: "ddt-01",
            version: 7,
            state: "Failed",
            message: "Port 67 is in use.",
            updatedUtc: hoursAgo(1),
          },
        ],
      },
      {
        section: "proxies",
        kind: "Restart",
        version: 1,
        updatedUtc: null,
        updatedBy: null,
        lockedCount: 0,
        problemCount: 0,
        apply: [],
      },
      {
        section: "certificate",
        kind: "Live",
        version: 1,
        updatedUtc: null,
        updatedBy: null,
        lockedCount: 0,
        problemCount: 0,
        apply: null,
      },
    ],
    server: [
      {
        key: "ConnectionStrings:ddtdb",
        value: "PostgreSQL, host db, database ddt",
        isSet: true,
        source: "environment variable",
        secret: true,
      },
      { key: "DDT:StorePath", value: "/var/lib/ddt", isSet: false, source: null, secret: false },
      {
        key: "Kestrel:Certificates:Default:Path",
        value: "/var/lib/ddt/certs/ddt.pem",
        isSet: true,
        source: "appsettings.json",
        secret: false,
      },
      {
        key: "Kestrel:Certificates:Default:KeyPath",
        value: "/var/lib/ddt/certs/ddt-key.pem",
        isSet: true,
        source: "appsettings.json",
        secret: false,
      },
      {
        key: "Kestrel:Certificates:Default:Password",
        value: null,
        isSet: false,
        source: null,
        secret: true,
      },
      {
        key: "DDT:Https:SubjectAlternativeNames",
        value: null,
        isSet: true,
        source: "environment variable",
        secret: false,
      },
      { key: "DDT:Agent:BinaryPath", value: null, isSet: false, source: null, secret: false },
    ],
    keyRingReadable: true,
    ...overrides,
  };
}

export function sectionView<T>(
  section: string,
  values: T,
  overrides: Partial<SettingsSectionView<T>> = {},
): SettingsSectionView<T> {
  return {
    section,
    version: 3,
    updatedUtc: hoursAgo(2),
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

export function namesView(
  names: string[] = ["ddt.corp.example"],
  overrides: Partial<SettingsSectionView<CertificateSettings>> = {},
): SettingsSectionView<CertificateSettings> {
  return sectionView(
    "certificate",
    { subjectAlternativeNames: names },
    { reauthenticate: ["subjectAlternativeNames"], ...overrides },
  );
}

export function certificateView(overrides: Partial<CertificateView> = {}): CertificateView {
  return {
    manageable: true,
    notManageable: null,
    served: {
      managedByDdt: true,
      subject: "CN=ddt.corp.example",
      sha256: "AA11BB22CC33DD44EE55FF66AA11BB22CC33DD44EE55FF66AA11BB22CC33DD44",
      notAfter: new Date(Date.now() + 200 * 86_400_000).toISOString(),
      renewsUtc: new Date(Date.now() + 170 * 86_400_000).toISOString(),
      names: ["localhost", "DDT-01", "ddt.corp.example"],
      rootSubject: "CN=DDT Root",
      rootSha256: "0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F",
      rootNotAfter: new Date(Date.now() + 7000 * 86_400_000).toISOString(),
      anchorReplacedUtc: null,
    },
    provisionalUntil: null,
    servedHere: true,
    canGenerate: true,
    hasRoot: true,
    names: namesView(),
    ...overrides,
  };
}

export function agentView(overrides: Partial<AgentBinaryView> = {}): AgentBinaryView {
  return {
    sha256: null,
    size: null,
    uploadedUtc: null,
    uploadedBy: null,
    source: "None",
    ...overrides,
  };
}

// The Server page on one of its tabs, on a fake server that answers the overview unless a test says otherwise.
export function serveServer(
  handlers: Record<string, Handler>,
  { tab = "overview", user = administrator }: { tab?: ServerTab; user?: CurrentUser } = {},
) {
  return servePage({
    user,
    path: "/admin/server",
    search: tab === "overview" ? "" : `?tab=${tab}`,
    component: ServerPage,
    handlers: { "GET /api/settings": () => json(overview()), ...handlers },
  });
}

// Runs axe over the page as the shell shows it, inside main: servePage renders the page alone, and axe wants page
// content inside a landmark. Dialogs stay where React Aria puts them.
export async function expectAccessible(): Promise<void> {
  const main = document.createElement("main");
  const pages = [...document.body.children].filter(
    (element) => element.tagName === "DIV" && element.querySelector("h1") !== null,
  );

  document.body.prepend(main);
  main.append(...pages);
  onTestFinished(() => {
    main.remove();
  });

  await expectNoAxeViolations();
}

export function sent(requests: readonly Sent[], method: string, path: string): Sent[] {
  return requests.filter((request) => request.method === method && request.path === path);
}

// Answers the password dialog, which a test expects the server to have asked for.
export async function typePassword(password = "secret"): Promise<void> {
  const proof = await screen.findByRole("dialog", { name: "Confirm it is you" });

  fireEvent.change(within(proof).getByLabelText("Password"), { target: { value: password } });
  fireEvent.click(within(proof).getByRole("button", { name: /^Confirm and / }));
  await waitFor(() => {
    expect(screen.queryByRole("dialog", { name: "Confirm it is you" })).not.toBeInTheDocument();
  });
}

// The token a fake server hands out, valid for as long as the real one.
export function proof(token: string): Response {
  return json({ token, expiresUtc: new Date(Date.now() + 300_000).toISOString() });
}

// A refusal of a request that changes something needing the password again.
export function passwordAgain(field: string): Response {
  return json(
    { title: `Enter your password again to change ${field}.`, status: 403, fields: [field] },
    403,
  );
}
