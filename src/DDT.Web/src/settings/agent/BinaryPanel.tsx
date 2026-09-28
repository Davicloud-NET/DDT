// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import type { Binary } from "./binary";
import { BinaryUpload } from "./BinaryUpload";
import { CurrentBinary } from "./CurrentBinary";
import { UploadsOff } from "./UploadsOff";

// The file machines take at their next netboot, and its upload unless configuration names it.
export function BinaryPanel({ binary }: { binary: Binary }) {
  const current = useQuery(binary.query);

  if (current.isError) {
    return <Notice tone="fail">{binary.unreadable}</Notice>;
  }

  if (current.data === undefined) {
    return <Skeleton className="h-64 w-full" />;
  }

  return (
    <>
      <CurrentBinary binary={binary} view={current.data} />
      {current.data.source === "Configuration" ? (
        <UploadsOff binary={binary} />
      ) : (
        <BinaryUpload binary={binary} />
      )}
    </>
  );
}
