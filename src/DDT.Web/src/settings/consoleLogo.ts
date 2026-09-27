// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";

// The logo the console at the machine shows at the right end of its header; the values are null when there is none.
export interface ConsoleLogoView {
  sha256: string | null;
  size: number | null;
  width: number | null;
  height: number | null;
  uploadedUtc: string | null;
  uploadedBy: string | null;
}

// The server takes a PNG of at most this size, and at most this many pixels wide and high.
export const maxLogoBytes = 512 * 1024;
export const maxLogoPixels = 2048;

// An upload or a removal answers with the view, and the hub's consoleLogoChanged brings it to other pages.
export const consoleLogoQuery = queryOptions({
  queryKey: ["settings-console-logo"],
  queryFn: () => apiGet<ConsoleLogoView>("/api/settings/console-logo"),
});

// The picture itself, named by its hash so a new logo is never taken from the browser's cache.
export function consoleLogoImage(sha256: string): string {
  return `/api/settings/console-logo/image?v=${encodeURIComponent(sha256)}`;
}

export async function uploadConsoleLogo(file: File): Promise<ConsoleLogoView> {
  return answer(
    await apiFetch("/api/settings/console-logo", {
      method: "PUT",
      headers: { "Content-Type": "image/png" },
      body: file,
    }),
  );
}

export async function removeConsoleLogo(): Promise<ConsoleLogoView> {
  return answer(await apiFetch("/api/settings/console-logo", { method: "DELETE" }));
}

async function answer(response: Response): Promise<ConsoleLogoView> {
  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as ConsoleLogoView;
}
