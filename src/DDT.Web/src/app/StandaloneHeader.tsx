// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Logo } from "@/ui/Logo";

// The frame bar of the pages outside the shell, such as sign-in and About: the mark and the name, nothing to navigate.
export function StandaloneHeader() {
  return (
    <header className="flex h-13 shrink-0 items-center gap-2.5 bg-frame px-5">
      <Logo size={22} />
      <span className="type-wordmark text-frame-text">DDT</span>
    </header>
  );
}
