// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import type { Findings } from "../problems";

// Each setting sits in an element named by its field, so the list of findings can move the focus there.
export interface FieldBase {
  label: ReactNode;
  // The field in the step, such as "script" or "conditions[1].value", as the server's findings name it.
  field: string;
  findings: Findings;
  hint?: ReactNode;
  className?: string;
}

export interface Choice {
  id: string;
  label: string;
  description?: string;
  isDisabled?: boolean;
}
