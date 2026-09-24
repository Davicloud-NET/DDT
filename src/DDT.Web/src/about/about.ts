// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

export interface AboutInfo {
  product: string;
  version: string;
  attribution: string;
  license: string;
  sourceUrl: string;
  // Paths in the server's legal folder, with forward slashes, such as licenses/web/THIRD-PARTY-LICENSES.txt.
  legalDocuments: string[];
}

export const ATTRIBUTION = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.";

export const SOURCE_URL = "https://github.com/Davicloud-NET/DDT";

// The server's answer changes only with a new version, which means a restart.
export const aboutQuery = queryOptions({
  queryKey: ["about"],
  staleTime: "static",
  queryFn: () => apiGet<AboutInfo>("/api/about"),
});

export function legalDocumentUrl(name: string): string {
  return `/api/about/legal/${name.split("/").map(encodeURIComponent).join("/")}`;
}
