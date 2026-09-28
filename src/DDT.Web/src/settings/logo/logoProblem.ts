// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formatBytes } from "@/lib/format";

import { maxLogoBytes } from "../consoleLogo";

// Why the server would refuse a file as the logo, said before anything is sent; null for a file it may take.
export function logoProblem(file: File): string | null {
  const name = file.name;
  const limit = formatBytes(maxLogoBytes);

  if (file.type !== "image/png" && !name.toLowerCase().endsWith(".png")) {
    return t`${name} is not a PNG image. Upload the logo as a PNG file.`;
  }

  if (file.size === 0) {
    return t`${name} is empty.`;
  }

  if (file.size > maxLogoBytes) {
    return t`${name} is larger than ${limit}, the most the server takes for the logo.`;
  }

  return null;
}
