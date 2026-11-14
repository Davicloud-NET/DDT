// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { useDebouncedValue } from "@/ui/useDebouncedValue";

import { keyIdDigits, recoveryKeyMachines } from "./machineRecords";

const NONE: readonly string[] = [];

// The machines whose recovery key ID is or starts with the search text, for text that can be one. Only the server
// knows the key IDs, so it is asked once the typing settles.
export function useRecoveryKeyMatches(query: string): readonly string[] {
  const digits = keyIdDigits(useDebouncedValue(query, 300));
  const matches = useQuery({
    queryKey: ["machines", "recovery-key-search", digits],
    queryFn: () => recoveryKeyMachines(digits ?? ""),
    enabled: digits !== null,
    placeholderData: keepPreviousData,
  });

  return digits === null ? NONE : (matches.data ?? NONE);
}
