// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { formattingLocale } from "@/i18n/i18n";

// The time of a save, as the builder and its conflict notice show it.
export function clockTime(time: number | string): string {
  return new Date(time).toLocaleTimeString(formattingLocale(), {
    hour: "2-digit",
    minute: "2-digit",
  });
}
