// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

// The result of a test, such as a directory sign-in's, on a well under the key that ran it.
export function ResultBox({ children }: { children: ReactNode }) {
  return <div className="flex flex-col gap-3 rounded-key bg-well p-3.5">{children}</div>;
}
