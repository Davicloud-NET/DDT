// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useRef, type KeyboardEvent, type ReactNode, type Ref } from "react";

import { isTextField } from "@/lib/textField";

import { cx } from "./cx";
import { Minimap, type MinimapItem } from "./Minimap";
import { useElementSize } from "./useElementSize";
import { usePanZoom } from "./usePanZoom";
import { usePointerGestures } from "./usePointerGestures";
import { useRevealHandle } from "./useRevealHandle";
import { useWheelGesture } from "./useWheelGesture";
import {
  centerOn,
  visibleContent,
  zoomKey,
  type ViewRect,
  type ViewTransform,
} from "./viewTransform";
import { ZoomControls } from "./ZoomControls";

// What a page can ask of the canvas. reveal shows a part of the content, moving as little as it takes, such as the
// node the keyboard went to. With center, it puts the part in the middle, such as a node a finding points at.
export interface FlowViewportHandle {
  reveal: (rect: ViewRect, center?: boolean) => void;
}

interface FlowViewportProps {
  label: string;
  contentWidth: number;
  contentHeight: number;
  transform?: ViewTransform;
  onTransformChange?: (next: ViewTransform) => void;
  // The parts the minimap draws, in the content's pixels. Without it, there's no minimap.
  minimap?: MinimapItem[];
  children: ReactNode;
  // More buttons next to the zoom controls, such as one that follows a run.
  controls?: ReactNode;
  className?: string;
  tabbable?: boolean;
  handle?: Ref<FlowViewportHandle>;
  // How the canvas first shows its content: all of it, or its top at a size that reads.
  start?: "fit" | "top";
}

// A canvas that pans and zooms a flow laid out in its own pixels, contentWidth by contentHeight. It's one Tab stop,
// unless tabbable is false. Then its content holds that stop, like the flow builder's nodes.
export function FlowViewport({
  label,
  contentWidth,
  contentHeight,
  transform,
  onTransformChange,
  minimap,
  children,
  controls,
  className,
  tabbable = true,
  handle,
  start = "fit",
}: FlowViewportProps) {
  const root = useRef<HTMLDivElement>(null);
  const size = useElementSize(root);
  const content = { width: contentWidth, height: contentHeight };
  const { view, latest, change, zoom } = usePanZoom({
    content,
    size,
    transform,
    onTransformChange,
    start,
  });
  const gestures = usePointerGestures(latest, change);

  useRevealHandle(handle, size, latest, change);
  useWheelGesture(root, size, latest, change);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const command = zoomKey(event);

    if (command === null || isTextField(event.target)) {
      return;
    }

    event.preventDefault();
    zoom(command);
  };

  const grid = 20 * view.scale;

  return (
    <div
      ref={root}
      role="group"
      aria-label={label}
      aria-keyshortcuts="+ - 0 1"
      {...(tabbable ? { tabIndex: 0 } : {})}
      {...gestures}
      onKeyDown={onKeyDown}
      className={cx(
        "relative touch-none overflow-hidden rounded-panel bg-well shadow-[inset_0_0_0_1px_var(--color-line)] select-none",
        "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus",
        className,
      )}
      style={{
        backgroundImage: "radial-gradient(var(--color-line) 1px, transparent 1.4px)",
        backgroundSize: `${String(grid)}px ${String(grid)}px`,
        backgroundPosition: `${String(view.x)}px ${String(view.y)}px`,
      }}
    >
      <div
        className="absolute top-0 left-0 origin-top-left"
        style={{
          width: contentWidth,
          height: contentHeight,
          transform: `translate(${String(view.x)}px, ${String(view.y)}px) scale(${String(view.scale)})`,
        }}
      >
        {children}
      </div>

      <ZoomControls scale={view.scale} onZoom={zoom}>
        {controls}
      </ZoomControls>

      {minimap !== undefined && size !== null ? (
        <Minimap
          className="absolute right-3 bottom-3"
          width={contentWidth}
          height={contentHeight}
          items={minimap}
          visible={visibleContent(view, size)}
          onNavigate={(point) => {
            change(centerOn(latest.current, point, size));
          }}
        />
      ) : null}
    </div>
  );
}
