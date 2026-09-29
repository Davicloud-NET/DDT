// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The first value of a distinguished name, such as "Deployment admins" from CN=Deployment admins,OU=Groups,DC=corp.
export function firstValue(distinguishedName: string): string {
  const first = distinguishedName.split(/(?<!\\),/)[0] ?? distinguishedName;

  return first.slice(first.indexOf("=") + 1).replace(/\\(.)/g, "$1");
}
