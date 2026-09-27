// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useSyncExternalStore } from "react";

// Whether a media query matches now, following it as the window changes. A page uses it where a narrow screen needs
// a different arrangement rather than the same one squeezed, such as a list in place of a table on a phone.
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);

      return () => {
        list.removeEventListener("change", onChange);
      };
    },
    () => window.matchMedia(query).matches,
    () => false,
  );
}
