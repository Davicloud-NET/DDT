// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

export function cx(...classes: (string | false | undefined)[]): string {
  return classes.filter((value): value is string => Boolean(value)).join(" ");
}
