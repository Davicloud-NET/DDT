// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { rowKey, type OpenRow } from "./declarationRows";

// Which rows of the variables panel are open. A row a finding points at opens, and can be closed again like any other.
export function useOpenRows(open: OpenRow | null) {
  const [opened, setOpened] = useState<readonly string[]>(
    open === null ? [] : [rowKey(open.list, open.index)],
  );
  const [pointed, setPointed] = useState(open);

  if (open !== pointed) {
    setPointed(open);

    if (open !== null) {
      const key = rowKey(open.list, open.index);

      setOpened((keys) => (keys.includes(key) ? keys : [...keys, key]));
    }
  }

  return {
    isOpen: (list: OpenRow["list"], index: number) => opened.includes(rowKey(list, index)),
    toggle: (list: OpenRow["list"], index: number) => {
      const key = rowKey(list, index);

      setOpened((keys) =>
        keys.includes(key) ? keys.filter((other) => other !== key) : [...keys, key],
      );
    },
    // A row just added opens.
    add: (list: OpenRow["list"], index: number) => {
      setOpened((keys) => [...keys, rowKey(list, index)]);
    },
  };
}
