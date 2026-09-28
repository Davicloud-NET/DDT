// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { createContext, useContext } from "react";

import type { AccountView } from "@/accounts/accounts";
import type { Subject } from "@/conditions/conditionSubjects";

import type { InputDeclaration, VariableDeclaration } from "../sequences";

// What the fields of the flow builder choose from and complete, beside the step catalog.
export interface BuilderData {
  subjects: Subject[];
  // Every name a template can use, in the order completion offers them: the sequence's own first.
  names: string[];
  known: (name: string) => boolean;
  // A name's value on the sample machine, with the sequence's defaults and the values of rules and roles.
  sample: (name: string) => string | null;
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
  // Null while the server lists none, as before it has accounts.
  accounts: AccountView[] | null;
}

export const BuilderContext = createContext<BuilderData>({
  subjects: [],
  names: [],
  known: () => true,
  sample: () => null,
  variables: [],
  inputs: [],
  accounts: null,
});

export function useBuilder(): BuilderData {
  return useContext(BuilderContext);
}
