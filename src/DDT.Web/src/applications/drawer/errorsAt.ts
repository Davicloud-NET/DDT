// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fieldFindings, type Findings } from "@/sequences/problems";

// The server's problems at a field, as a React Aria field takes them.
export function errorsAt(findings: Findings, field: string) {
  const problems = fieldFindings(findings, field).problems;

  return { isInvalid: problems.length > 0, errorMessage: problems.join(" ") };
}
