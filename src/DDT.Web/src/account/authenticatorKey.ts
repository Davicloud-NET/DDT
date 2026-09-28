// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// "ABCD EFGH IJKL", easier to type into an app than one long string.
export function groupKey(key: string): string {
  return key
    .replace(/\s+/g, "")
    .replace(/(.{4})/g, "$1 ")
    .trim();
}
