// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { PointerEvent } from "react";

import { cx } from "./cx";
import type { ViewPoint, ViewRect } from "./viewTransform";

// A part of the content as the minimap draws it: a frame, a node, or a node the run did not take.
export interface MinimapItem extends ViewRect {
  tone: "frame" | "node" | "muted";
}

const fills: Record<MinimapItem["tone"], string> = {
  frame: "fill-line-soft",
  node: "fill-control",
  muted: "fill-line",
};

// The whole content in small, with the part the canvas shows outlined. Pressing or dragging on it moves the canvas
// there. It is for the pointer only and hidden from screen readers, who move through the flow itself.
export function Minimap({
  width,
  height,
  items,
  visible,
  onNavigate,
  maxWidth = 176,
  maxHeight = 112,
  className,
}: {
  width: number;
  height: number;
  items: readonly MinimapItem[];
  // The part the canvas shows, in the content's pixels.
  visible: ViewRect;
  // Asks the canvas to put this point of the content in its middle.
  onNavigate: (point: ViewPoint) => void;
  maxWidth?: number;
  maxHeight?: number;
  className?: string;
}) {
  const scale = width > 0 && height > 0 ? Math.min(maxWidth / width, maxHeight / height) : 1;
  const shown = { width: Math.max(1, width * scale), height: Math.max(1, height * scale) };
  // The outline stays inside the minimap when the canvas shows more than the content.
  const left = Math.max(0, visible.x);
  const top = Math.max(0, visible.y);
  const outline = {
    x: left * scale,
    y: top * scale,
    w: (Math.min(width, visible.x + visible.w) - left) * scale,
    h: (Math.min(height, visible.y + visible.h) - top) * scale,
  };

  const navigate = (event: PointerEvent<SVGSVGElement>) => {
    const bounds = event.currentTarget.getBoundingClientRect();

    onNavigate({
      x: (event.clientX - bounds.left) / scale,
      y: (event.clientY - bounds.top) / scale,
    });
  };

  return (
    <svg
      aria-hidden="true"
      data-no-pan
      width={shown.width}
      height={shown.height}
      className={cx(
        "cursor-pointer touch-none rounded-key bg-raised shadow-[inset_0_0_0_1px_var(--color-line)]",
        className,
      )}
      onPointerDown={(event) => {
        event.currentTarget.setPointerCapture(event.pointerId);
        navigate(event);
      }}
      onPointerMove={(event) => {
        if (event.currentTarget.hasPointerCapture(event.pointerId)) {
          navigate(event);
        }
      }}
    >
      {items.map((item, index) => (
        <rect
          key={index}
          x={item.x * scale}
          y={item.y * scale}
          width={Math.max(1, item.w * scale)}
          height={Math.max(1, item.h * scale)}
          rx={1}
          className={fills[item.tone]}
        />
      ))}
      {outline.w > 0 && outline.h > 0 ? (
        <rect
          x={outline.x + 0.75}
          y={outline.y + 0.75}
          width={Math.max(0, outline.w - 1.5)}
          height={Math.max(0, outline.h - 1.5)}
          rx={2}
          className="fill-none stroke-focus"
          strokeWidth={1.5}
        />
      ) : null}
    </svg>
  );
}
