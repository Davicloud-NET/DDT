// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";

const CODED = /^#([A-Za-z0-9]+)(?: (\d+))?$/;

// A step detail the agent sends coded, "#downloadingUpdates 5", in the reader's words. Anything else, such as an
// application's name, as it is.
export function stepDetailText(detail: string): string {
  const match = CODED.exec(detail);
  const count = match?.[2] === undefined ? null : Number(match[2]);

  if (match === null || count === null) {
    return detail;
  }

  switch (match[1]) {
    case "downloadingUpdates":
      return plural(count, { one: "Downloading # update", other: "Downloading # updates" });
    case "installingUpdates":
      return plural(count, { one: "Installing # update", other: "Installing # updates" });
    default:
      return detail;
  }
}
