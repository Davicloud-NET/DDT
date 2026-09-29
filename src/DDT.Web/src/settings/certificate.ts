// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";
import { serverCertificateQuery, type ServerCertificateView } from "@/server/serverCertificate";

import {
  certificateKey,
  reauthenticationToken,
  settingsKey,
  type SettingsSectionView,
} from "./settings";

// The server names: the names the server is reached by. Generate issues a certificate for them, and an uploaded
// one must cover them.
export interface CertificateSettings {
  subjectAlternativeNames: string[];
}

// The certificate as GET /api/settings/certificate answers it.
export interface CertificateView {
  manageable: boolean;
  // Why the page cannot change the certificate, while manageable is false.
  notManageable: string | null;
  served: ServerCertificateView | null;
  // Set while a new pair waits to be confirmed. The confirmation must come from a connection that was served it.
  provisionalUntil: string | null;
  // Whether this page's connection was served the current certificate, or null without TLS. A hub update keeps
  // the page's own value, since it's per connection.
  servedHere: boolean | null;
  canGenerate: boolean;
  hasRoot: boolean;
  names: SettingsSectionView<CertificateSettings>;
}

// Either the PEM chain and its key, or a PFX in base64 with its password.
export type CertificateUpload =
  { certificatePem: string; keyPem: string } | { pfx: string; pfxPassword: string | null };

// The confirmation an upload or a Generate needs when the pair does not come from DDT's root.
export const newRootCode = "certificate.newRoot";

// The server names section is also cached under its own key, for its form and the hub's settingsChanged. Reading
// the certificate fills that key, so the form doesn't read the certificate a second time.
export const certificateQuery = queryOptions({
  queryKey: certificateKey,
  queryFn: async ({ client }) => {
    const view = await apiGet<CertificateView>("/api/settings/certificate");

    putNames(client, view);

    return view;
  },
});

function putNames(queryClient: QueryClient, view: CertificateView): void {
  const key = settingsKey("certificate");
  const cached = queryClient.getQueryData<SettingsSectionView<CertificateSettings>>(key);

  if (cached === undefined || cached.version <= view.names.version) {
    queryClient.setQueryData(key, view.names);
  }
}

// An action answers with the certificate as it's served now. The Boot image page shows it too.
export function putCertificate(queryClient: QueryClient, view: CertificateView): void {
  queryClient.setQueryData(certificateKey, view);
  putNames(queryClient, view);

  if (view.served !== null) {
    queryClient.setQueryData(serverCertificateQuery.queryKey, view.served);
  }
}

async function post(path: string, body: unknown): Promise<CertificateView> {
  const token = reauthenticationToken();
  const response = await apiFetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as CertificateView;
}

export function uploadCertificate(
  upload: CertificateUpload,
  confirm: string[],
): Promise<CertificateView> {
  return post("/api/settings/certificate", { ...upload, confirm });
}

export function generateCertificate(confirm: string[]): Promise<CertificateView> {
  return post("/api/settings/certificate/generate", { confirm });
}

export function confirmCertificate(): Promise<CertificateView> {
  return post("/api/settings/certificate/confirm", {});
}

// A PFX file as base64, the way the upload sends it.
export async function base64Of(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = "";

  for (let start = 0; start < bytes.length; start += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(start, start + 0x8000));
  }

  return btoa(binary);
}
