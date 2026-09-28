// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { InputDeclaration, VariableDeclaration } from "@/sequences/sequences";

// Rules and machine roles don't belong to a sequence. They don't declare variables or ask for inputs.
export const noDeclarations: {
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
} = { variables: [], inputs: [] };
