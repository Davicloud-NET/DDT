// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The row around an element found by what one of its cells shows, such as the link of a machine's name.
export function rowOf(element: Element, selector = "[role=row]"): HTMLElement {
  const found = element.closest<HTMLElement>(selector);

  if (found === null) {
    throw new Error(`${element.textContent} is not in a row.`);
  }

  return found;
}
