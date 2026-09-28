// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useState } from "react";

// Whether the command palette is open; Ctrl K (Cmd K on a Mac) opens and closes it from anywhere on the page.
export function usePaletteOpen() {
  const [isOpen, setOpen] = useState(false);

  useEffect(() => {
    const open = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === "k" && (event.ctrlKey || event.metaKey) && !event.altKey) {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };

    window.addEventListener("keydown", open);

    return () => {
      window.removeEventListener("keydown", open);
    };
  }, []);

  return { isOpen, setOpen };
}
