// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useState, type RefObject } from "react";

import type { ViewSize } from "./viewTransform";

// The inner size of an element, null until it is first measured.
export function useElementSize(ref: RefObject<HTMLElement | null>): ViewSize | null {
  const [size, setSize] = useState<ViewSize | null>(null);

  // A ResizeObserver reports the size once when it starts observing.
  useEffect(() => {
    const element = ref.current;

    if (element === null) {
      return;
    }

    const measure = () => {
      setSize({ width: element.clientWidth, height: element.clientHeight });
    };

    if (typeof ResizeObserver === "undefined") {
      measure();
      return;
    }

    const observer = new ResizeObserver(measure);
    observer.observe(element);

    return () => {
      observer.disconnect();
    };
  }, [ref]);

  return size;
}
