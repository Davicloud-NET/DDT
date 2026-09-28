// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useEffectEvent, useRef, useState } from "react";

import type { FlowLayout } from "@/sequences/flow/flowGeometry";
import type { FlowViewportHandle } from "@/ui/FlowViewport";

export type RunFollow = ReturnType<typeof useFollowRun>;

// The canvas follows the node the run is at, until something else moves the canvas. setFollowing(true) starts following
// again.
export function useFollowRun(layout: FlowLayout, currentId: string | null) {
  const [following, setFollowing] = useState(true);
  const viewport = useRef<FlowViewportHandle>(null);
  // The canvas moves itself while it follows the run. Any other move comes from the person and stops the following.
  const moving = useRef(false);

  const follow = useEffectEvent((id: string) => {
    const box = layout.boxes.find((candidate) => candidate.id === id);

    if (box !== undefined && viewport.current !== null) {
      moving.current = true;
      viewport.current.reveal(box, true);
    }
  });

  // A run that ended shows the node it failed at, until the person moves the canvas.
  useEffect(() => {
    if (currentId !== null && following) {
      follow(currentId);
    }
  }, [currentId, following]);

  const onTransformChange = () => {
    if (moving.current) {
      moving.current = false;
    } else {
      setFollowing(false);
    }
  };

  return { viewport, following, setFollowing, onTransformChange };
}
