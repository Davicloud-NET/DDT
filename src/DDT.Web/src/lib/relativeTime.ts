// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { formattingLocale } from "@/i18n/i18n";

// "5 minutes ago", "vor 5 Minuten": in the language chosen in DDT.
export function relativeTime(iso: string, now: number): string {
  const formatter = new Intl.RelativeTimeFormat(formattingLocale(), { numeric: "auto" });
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);

  if (Math.abs(seconds) < 60) {
    return formatter.format(seconds, "second");
  }

  const minutes = Math.round(seconds / 60);

  if (Math.abs(minutes) < 60) {
    return formatter.format(minutes, "minute");
  }

  const hours = Math.round(minutes / 60);

  if (Math.abs(hours) < 24) {
    return formatter.format(hours, "hour");
  }

  return formatter.format(Math.round(hours / 24), "day");
}
