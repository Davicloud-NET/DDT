// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Skeleton } from "@/ui/Skeleton";

export function LoadingRows() {
  return (
    <div aria-hidden="true" className="flex flex-col">
      <div className="h-10 border-b border-line" />
      {Array.from({ length: 4 }, (_, index) => (
        <div key={index} className="flex h-14 items-center gap-6 border-b border-line-soft px-4">
          <span className="flex w-72 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-44" />
            <Skeleton className="h-3 w-60" />
          </span>
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-3.5 flex-1" />
        </div>
      ))}
    </div>
  );
}
