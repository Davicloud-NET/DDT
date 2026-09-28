// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

// A list's search field and the items it leaves. matches gets the search in lower case, without spaces around it.
export function useListSearch<T>(items: T[], matches: (item: T, needle: string) => boolean) {
  const [query, setQuery] = useState("");
  const needle = query.trim().toLowerCase();
  const shown = needle === "" ? items : items.filter((item) => matches(item, needle));

  return { query, setQuery, shown };
}
