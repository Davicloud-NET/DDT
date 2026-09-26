// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMemo } from "react";
import { encode } from "uqr";

import { cx } from "./cx";

// Cameras need the quiet zone of four modules around the code.
const QUIET_ZONE = 4;

// A QR code, dark on white in both themes, because phone cameras read that most reliably. Every dark module is one
// square of a single path, so the code stays sharp at any size.
export function QrCode({
  value,
  label,
  className,
}: {
  value: string;
  // What the code holds, in words, for screen readers.
  label: string;
  className?: string;
}) {
  const { size, path } = useMemo(() => {
    const code = encode(value, { ecc: "M" });
    const squares: string[] = [];

    code.data.forEach((row, y) => {
      row.forEach((dark, x) => {
        if (dark) {
          squares.push(`M${String(x + QUIET_ZONE)} ${String(y + QUIET_ZONE)}h1v1h-1z`);
        }
      });
    });

    return { size: code.size + 2 * QUIET_ZONE, path: squares.join("") };
  }, [value]);

  return (
    <svg
      role="img"
      aria-label={label}
      viewBox={`0 0 ${String(size)} ${String(size)}`}
      shapeRendering="crispEdges"
      className={cx("block rounded-key bg-white", className)}
    >
      <path d={path} fill="#1F2426" />
    </svg>
  );
}
