// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, type UseMutationResult } from "@tanstack/react-query";
import { useCallback, useState } from "react";

export interface Deletion<T> {
  // The entry the dialog asks about, or null while it is closed.
  item: T | null;
  ask: (item: T) => void;
  close: () => void;
  mutation: UseMutationResult<void, Error, string>;
}

// The entry a delete dialog asks about and the request that deletes it once confirmed. onDeleted patches the
// cache, since the server answers a delete with nothing but its status.
export function useDeletion<T>(
  request: (id: string) => Promise<void>,
  onDeleted: (id: string) => void,
): Deletion<T> {
  const [item, setItem] = useState<T | null>(null);
  const mutation = useMutation({
    mutationFn: (id: string) => request(id),
    onSuccess: (_, id) => {
      onDeleted(id);
      setItem(null);
    },
  });
  const { reset } = mutation;

  // Stable, so a table's rows may keep it without listing it in their dependencies.
  const ask = useCallback(
    (next: T) => {
      reset();
      setItem(next);
    },
    [reset],
  );

  return {
    item,
    ask,
    close: () => {
      setItem(null);
    },
    mutation,
  };
}
