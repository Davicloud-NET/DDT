// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { fill } from "@/test/aria";
import { json, reads } from "@/test/serve";

import type { CertificateSettings } from "./certificate";
import {
  certificateView,
  expectAccessible,
  namesView,
  overview,
  passwordAgain,
  proof,
  sent,
  serveServer,
  typePassword,
} from "./serverTesting";
import type { SettingsSectionUpdate } from "./settings";

const newRoot = {
  title: "One or more validation errors occurred.",
  status: 400,
  errors: {
    confirm: [
      "certificate.newRoot: DDT has no root yet, so Generate makes one: build every boot image again with it, and trust it in the browsers that manage DDT.",
    ],
  },
  confirm: [
    {
      field: "",
      message:
        "DDT has no root yet, so Generate makes one: build every boot image again with it, and trust it in the browsers that manage DDT.",
      code: "certificate.newRoot",
    },
  ],
};

function inMinutes(minutes: number): string {
  return new Date(Date.now() + minutes * 60_000).toISOString();
}

describe("CertificatePanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the served certificate and its names", async () => {
    serveServer(
      { "GET /api/settings/certificate": () => json(certificateView()) },
      { tab: "certificate" },
    );

    expect(await screen.findByText("CN=ddt.corp.example")).toBeInTheDocument();
    expect(screen.getByText("DDT's root, CN=DDT Root")).toBeInTheDocument();
    expect(screen.getByText("localhost, DDT-01, ddt.corp.example")).toBeInTheDocument();
    expect(screen.getByText("Was served this certificate.")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Names and addresses" })).toHaveValue(
      "ddt.corp.example",
    );
    expect(screen.getByRole("button", { name: "Generate certificate" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Upload certificate" })).toBeEnabled();

    await expectAccessible();
  });

  it("keeps a certificate on trial, and shows the answer without reading it again", async () => {
    const trial = certificateView({ provisionalUntil: inMinutes(4), servedHere: true });
    const { requests, queryClient } = serveServer(
      {
        "GET /api/settings/certificate": () => json(trial),
        "POST /api/settings/certificate/confirm": () => json({ ...trial, provisionalUntil: null }),
      },
      { tab: "certificate" },
    );

    expect(await screen.findByText(/The new certificate is on trial until/)).toBeInTheDocument();
    expect(
      screen.getByText(/If nobody keeps it by .*, DDT goes back to the certificate before/),
    ).toBeInTheDocument();
    await expectAccessible();

    const before = reads(requests, "/api/settings/certificate");
    fireEvent.click(screen.getByRole("button", { name: "Keep the new certificate" }));

    await waitFor(() => {
      expect(screen.queryByText(/The new certificate is on trial until/)).not.toBeInTheDocument();
    });
    expect(sent(requests, "POST", "/api/settings/certificate/confirm")).toHaveLength(1);
    expect(reads(requests, "/api/settings/certificate")).toBe(before);
    // The Boot image page reads the served certificate under its own key, and gets the same.
    expect(queryClient.getQueryData(["server-certificate"])).toEqual(trial.served);
  });

  it("says why keeping it failed when this connection still has the certificate before", async () => {
    serveServer(
      {
        "GET /api/settings/certificate": () =>
          json(certificateView({ provisionalUntil: inMinutes(4), servedHere: false })),
        "POST /api/settings/certificate/confirm": () =>
          json(
            {
              title:
                "This connection was served the certificate before the new one, so it proves nothing about the new one. Load the page again, which connects anew, and confirm from there.",
              status: 409,
            },
            409,
          ),
      },
      { tab: "certificate" },
    );

    expect(
      await screen.findByText("Was served the certificate before; its next request gets this one."),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Keep the new certificate" }));

    expect(
      await screen.findByText(/This connection was served the certificate before the new one/),
    ).toBeInTheDocument();
  });

  it("saves the server names with the password again", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/certificate": () => json(certificateView()),
        "POST /api/settings/reauthenticate": () => proof("names-proof"),
        "PUT /api/settings/certificate/names": (request) =>
          request.headers["x-ddt-reauthentication"] === "names-proof"
            ? json(
                namesView(
                  (request.body as SettingsSectionUpdate<CertificateSettings>).values
                    .subjectAlternativeNames,
                  { version: 4, updatedUtc: new Date().toISOString(), updatedBy: "Ada Admin" },
                ),
              )
            : passwordAgain("subjectAlternativeNames"),
      },
      { tab: "certificate" },
    );

    const names = await screen.findByRole("textbox", { name: "Names and addresses" });
    const before = reads(requests, "/api/settings/certificate");
    fill(names, "ddt.corp.example\nddt\n10.0.0.5");
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await typePassword();

    expect(await screen.findByText("Saved now by Ada Admin.")).toBeInTheDocument();
    const puts = sent(requests, "PUT", "/api/settings/certificate/names");
    // The token of an earlier test may still be held, which this server refuses as well.
    expect(
      puts.map((request) => request.headers["x-ddt-reauthentication"] === "names-proof"),
    ).toEqual([false, true]);
    expect((puts[1]?.body as SettingsSectionUpdate<CertificateSettings>).values).toEqual({
      subjectAlternativeNames: ["ddt.corp.example", "ddt", "10.0.0.5"],
    });
    expect(reads(requests, "/api/settings/certificate")).toBe(before);
  });

  it("generates a certificate after confirming a new root and the password, and puts it on trial", async () => {
    const { requests } = serveServer(
      {
        "GET /api/settings/certificate": () => json(certificateView({ hasRoot: false })),
        "POST /api/settings/reauthenticate": () => proof("generate-proof"),
        "POST /api/settings/certificate/generate": (request) => {
          const confirm = (request.body as { confirm: string[] }).confirm;

          if (!confirm.includes("certificate.newRoot")) {
            return json(newRoot, 400);
          }

          if (request.headers["x-ddt-reauthentication"] !== "generate-proof") {
            return passwordAgain("certificate");
          }

          return json(
            certificateView({ hasRoot: true, provisionalUntil: inMinutes(5), servedHere: false }),
          );
        },
      },
      { tab: "certificate" },
    );

    fireEvent.click(await screen.findByRole("button", { name: "Generate certificate" }));
    const before = reads(requests, "/api/settings/certificate");
    const dialog = await screen.findByRole("dialog", { name: "Generate a new certificate?" });
    expect(within(dialog).getByText(/DDT has no root yet, so it makes one/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Generate certificate" }));

    const root = await screen.findByRole("dialog", { name: "Make a new root?" });
    await expectAccessible();
    fireEvent.click(within(root).getByRole("button", { name: "Make a new root" }));

    await typePassword();

    expect(await screen.findByText(/The new certificate is on trial until/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    const posts = sent(requests, "POST", "/api/settings/certificate/generate");
    expect(posts.map((request) => (request.body as { confirm: string[] }).confirm)).toEqual([
      [],
      ["certificate.newRoot"],
      ["certificate.newRoot"],
    ]);
    expect(
      posts.map((request) => request.headers["x-ddt-reauthentication"] === "generate-proof"),
    ).toEqual([false, false, true]);
    expect(reads(requests, "/api/settings/certificate")).toBe(before);
  });

  it("uploads a PEM pair, and marks the field the server refused", async () => {
    let refused = false;
    const { requests } = serveServer(
      {
        "GET /api/settings/certificate": () => json(certificateView()),
        "POST /api/settings/reauthenticate": () => proof("upload-proof"),
        "POST /api/settings/certificate": (request) => {
          if (request.headers["x-ddt-reauthentication"] !== "upload-proof") {
            return passwordAgain("certificate");
          }

          if (!refused) {
            refused = true;

            return json(
              {
                title: "One or more validation errors occurred.",
                status: 400,
                errors: { keyPem: ["The certificate does not load with this key: bad key."] },
              },
              400,
            );
          }

          return json(certificateView({ provisionalUntil: inMinutes(5) }));
        },
      },
      { tab: "certificate" },
    );

    fireEvent.click(await screen.findByRole("button", { name: "Upload certificate" }));
    const dialog = await screen.findByRole("dialog", { name: "Upload a certificate" });
    const upload = within(dialog).getByRole("button", { name: "Upload certificate" });
    expect(upload).toBeDisabled();

    fill(
      within(dialog).getByRole("textbox", { name: "Certificate, followed by its intermediates" }),
      "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----",
    );
    fill(
      within(dialog).getByRole("textbox", { name: "Private key" }),
      "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----",
    );
    await expectAccessible();
    fireEvent.click(upload);

    await typePassword();

    expect(
      await within(dialog).findByText("The certificate does not load with this key: bad key."),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole("textbox", { name: "Private key" })).toHaveAttribute(
      "aria-invalid",
      "true",
    );

    fireEvent.click(upload);

    expect(await screen.findByText(/The new certificate is on trial until/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(
      sent(requests, "POST", "/api/settings/certificate").map((request) => request.body),
    ).toEqual([
      ...Array<unknown>(3).fill({
        certificatePem: "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----",
        keyPem: "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----",
        confirm: [],
      }),
    ]);
  });

  it("says why the page cannot change a certificate with a password in configuration", async () => {
    serveServer(
      {
        "GET /api/settings": () =>
          json(
            overview({
              server: [
                {
                  key: "Kestrel:Certificates:Default:Path",
                  value: "/certs/ddt.pfx",
                  isSet: true,
                  source: "appsettings.json",
                  secret: false,
                },
                {
                  key: "Kestrel:Certificates:Default:Password",
                  value: null,
                  isSet: true,
                  source: "environment variable",
                  secret: true,
                },
              ],
            }),
          ),
        "GET /api/settings/certificate": () =>
          json(
            certificateView({
              manageable: false,
              notManageable: "The page manages the certificate only when ...",
              served: null,
              canGenerate: false,
            }),
          ),
      },
      { tab: "certificate" },
    );

    expect(
      await screen.findByText(/Kestrel:Certificates:Default:Password is set in configuration/),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate certificate" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Upload certificate" })).not.toBeInTheDocument();
  });
});
