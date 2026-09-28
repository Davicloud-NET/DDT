// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { cx } from "./cx";
import { Skeleton } from "./Skeleton";

// The lines a list shows while it loads, one per width. A panel with padding of its own takes them unpadded.
export function ListSkeleton({
  widths = ["w-1/2", "w-2/3"],
  padded = true,
}: {
  widths?: readonly string[];
  padded?: boolean;
}) {
  const lines = widths.map((width, index) => <Skeleton key={index} className={cx("h-6", width)} />);

  return padded ? <div className="flex flex-col gap-3 p-4">{lines}</div> : lines;
}
