// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { ChangedBy } from "@/ui/ChangedBy";

// The Uploaded column of the image and package lists.
export function Uploaded({
  entry,
  now,
}: {
  entry: { uploadedUtc: string; uploadedBy: string | null };
  now: number;
}) {
  const when = relativeTime(entry.uploadedUtc, now);
  const by = entry.uploadedBy;

  return <ChangedBy when={when} by={by} title={fullTime(entry.uploadedUtc)} />;
}
