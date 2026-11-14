// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

const CODED = /^#([A-Za-z0-9]+)(?: (.+))?$/;

// A step detail the agent sends coded, "#downloadingUpdates 5" or "#addingPackage de-DE language pack", in the
// reader's words. Anything else, such as an application's name, as it is.
export function stepDetailText(detail: string): string {
  const match = CODED.exec(detail);

  if (match === null) {
    return detail;
  }

  const text = match[2] ?? null;
  const count = text !== null && /^\d+$/.test(text) ? Number(text) : null;

  switch (match[1]) {
    case "downloadingUpdates":
      return count === null
        ? detail
        : plural(count, { one: "Downloading # update", other: "Downloading # updates" });
    case "installingUpdates":
      return count === null
        ? detail
        : plural(count, { one: "Installing # update", other: "Installing # updates" });
    case "addingPackage":
      return text === null ? detail : t`Adding ${text}`;
    case "cleaningUp":
      return text === null ? t`Cleaning up the image` : detail;
    default:
      return detail;
  }
}
