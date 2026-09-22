// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { ApiError, apiDelete, apiGet } from "@/lib/api";

// managedByDdt: issued from DDT's root and renewed by DDT; otherwise an administrator's certificate, and the root
// fields and renewsUtc are null. anchorReplacedUtc: when DDT replaced the self-signed certificate that older boot
// images pin, until an administrator confirms every boot image was built again with the root.
export interface ServerCertificateView {
  managedByDdt: boolean;
  subject: string;
  sha256: string;
  notAfter: string;
  renewsUtc: string | null;
  names: string[];
  rootSubject: string | null;
  rootSha256: string | null;
  rootNotAfter: string | null;
  anchorReplacedUtc: string | null;
}

// Null when Kestrel loads the certificate on its own, or TLS ends at a proxy: DDT then knows nothing about it.
export const serverCertificateQuery = queryOptions({
  queryKey: ["server-certificate"],
  queryFn: async (): Promise<ServerCertificateView | null> => {
    try {
      return await apiGet<ServerCertificateView>("/api/server/certificate");
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return null;
      }

      throw error;
    }
  },
  staleTime: 5 * 60_000,
});

// Ends the notice for every administrator, and the server deletes its copy of the replaced certificate.
export function acknowledgeReplacedAnchor(): Promise<void> {
  return apiDelete("/api/server/certificate/replaced-anchor");
}
