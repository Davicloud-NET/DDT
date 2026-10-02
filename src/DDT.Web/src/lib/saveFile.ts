// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Hands the browser a file the page fetched itself, for a download that needs a header a link cannot send.
export function saveFile(content: Blob, name: string): void {
  const address = URL.createObjectURL(content);
  const link = document.createElement("a");

  link.href = address;
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(address);
}
