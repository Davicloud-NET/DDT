// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// What a sequence declares besides its steps: its variables and the inputs it asks before a run starts.

// An application or a bundle of the library, as a step or an input names it.
export interface ApplicationReference {
  id: string;
  kind: "Application" | "Bundle";
}

// A value the sequence uses, such as ComputerName or Office. default is a template. Only a variable with setBySteps
// may be changed by a Set variable step or a script's output during the run.
export interface VariableDeclaration {
  name: string;
  default: string | null;
  description: string | null;
  setBySteps: boolean;
}

// Applications (version 6) is a tick list of applications and bundles. Its answer is their ids, separated by ";".
export type InputKind = "Text" | "Choice" | "MultiChoice" | "YesNo" | "Account" | "Applications";

export type InputAsk = "Both" | "Web" | "Machine";

// label is what the person sees, and value is what the answer sets. A null label shows the value.
export interface InputChoice {
  value: string;
  label: string | null;
}

// Where an Account input's account may be used: the domain it joins, the share hosts it may connect to, and
// whether a script may run as it.
export interface AccountDestination {
  domain: string | null;
  hosts: string[];
  runAs: boolean;
}

// A question asked before the run starts, on the web, at the machine or both. name is the variable its answer sets.
// An Account input doesn't set one, and steps name it in an AccountReference. choices are for Choice and MultiChoice.
export interface InputDeclaration {
  name: string;
  label: string;
  help: string | null;
  kind: InputKind;
  choices: InputChoice[];
  default: string | null;
  required: boolean;
  maxLength: number | null;
  askAt: InputAsk;
  account: AccountDestination | null;
  // Only an Applications input has them: what it offers, in order, and the ids that start ticked.
  offered?: ApplicationReference[] | null;
  ticked?: string[] | null;
}
