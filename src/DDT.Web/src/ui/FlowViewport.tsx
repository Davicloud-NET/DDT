// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconMinus, IconPlus } from "@tabler/icons-react";
import {
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
  type Ref,
} from "react";
import { Button } from "react-aria-components";

import { formattingLocale } from "@/i18n/i18n";
import { isTextField } from "@/lib/textField";

import { cx } from "./cx";
import { Minimap, type MinimapItem } from "./Minimap";
import {
  centerOn,
  clampPan,
  ensureVisible,
  fitTransform,
  IDENTITY,
  panBy,
  pinchChange,
  topTransform,
  visibleContent,
  wheelChange,
  zoomAt,
  zoomBy,
  zoomKey,
  ZOOM_STEP,
  type ViewPoint,
  type ViewRect,
  type ViewSize,
  type ViewTransform,
} from "./viewTransform";

// What a page asks of the canvas: to show a part of the content, such as the node the keyboard went to, moving as
// little as it takes, or in the middle, such as a node a finding points at.
export interface FlowViewportHandle {
  reveal: (rect: ViewRect, center?: boolean) => void;
}

// Where a drag or a pinch started, so each move is worked out from there rather than from the last render.
interface Gesture {
  view: ViewTransform;
  points: Map<number, ViewPoint>;
  start: Map<number, ViewPoint>;
}

// What a press on the canvas leaves alone: controls, and the nodes, which the flow drags itself.
const notPanned =
  'button, a, input, textarea, select, [role="button"], [data-flow-node], [data-no-pan]';

const controlClass =
  "flex h-full min-w-8 cursor-pointer items-center justify-center px-2 text-ink motion-colors outline-none " +
  "hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:-outline-offset-2 " +
  "focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45";

// A canvas for a flow: its content is moved by dragging the background or with the wheel, and zoomed with Ctrl and
// the wheel, a pinch, the keys + - 0 1 and the controls in its corner, which also fit the content in. The content is
// laid out in its own pixels, contentWidth by contentHeight, under the transform. The transform is the page's to
// keep when it passes one, and the canvas's own, starting fitted, otherwise. The canvas is a stop of the Tab key of its
// own unless tabbable is false, where its content holds that stop, such as the flow builder's nodes.
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
}: {
  label: string;
  contentWidth: number;
  contentHeight: number;
  transform?: ViewTransform;
  onTransformChange?: (next: ViewTransform) => void;
  // The parts the minimap draws, in the content's pixels; none leaves it out.
  minimap?: MinimapItem[];
  children: ReactNode;
  // More keys beside the zoom controls, such as one that follows a run.
  controls?: ReactNode;
  className?: string;
  tabbable?: boolean;
  handle?: Ref<FlowViewportHandle>;
  // How the canvas first shows its content: all of it, or its top at a size that reads.
  start?: "fit" | "top";
}) {
  const { t } = useLingui();
  const root = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState<ViewSize | null>(null);
  const [own, setOwn] = useState<ViewTransform | null>(null);
  const content = { width: contentWidth, height: contentHeight };
  const view =
    transform ??
    own ??
    (size === null
      ? IDENTITY
      : start === "top"
        ? clampPan(topTransform(content, size), content, size)
        : fitTransform(content, size));
  // The newest transform, for events that come faster than the page renders, such as the wheel's.
  const latest = useRef(view);
  const gesture = useRef<Gesture | null>(null);

  useEffect(() => {
    latest.current = view;
  });

  const change = (next: ViewTransform) => {
    const kept = size === null ? next : clampPan(next, content, size);

    latest.current = kept;

    if (transform === undefined) {
      setOwn(kept);
    }

    onTransformChange?.(kept);
  };

  // A ResizeObserver reports the size once when it starts observing.
  useEffect(() => {
    const element = root.current;

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
  }, []);

  const middle = (): ViewPoint =>
    size === null ? { x: 0, y: 0 } : { x: size.width / 2, y: size.height / 2 };

  const fit = () => {
    if (size !== null) {
      change(fitTransform(content, size));
    }
  };

  // A part asked for before the canvas knows its size is shown once it does.
  const pending = useRef<{ rect: ViewRect; center: boolean } | null>(null);

  const show = (rect: ViewRect, center: boolean, canvas: ViewSize) => {
    change(
      center
        ? centerOn(latest.current, { x: rect.x + rect.w / 2, y: rect.y + rect.h / 2 }, canvas)
        : ensureVisible(latest.current, rect, canvas),
    );
  };

  useImperativeHandle(handle, () => ({
    reveal: (rect, center = false) => {
      if (size === null) {
        pending.current = { rect, center };
      } else {
        show(rect, center, size);
      }
    },
  }));

  useEffect(() => {
    const asked = pending.current;

    if (asked !== null && size !== null) {
      pending.current = null;
      show(asked.rect, asked.center, size);
    }
    // Only a size, once known, shows what was asked for before it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [size]);

  const zoom = (command: "in" | "out" | "actual" | "fit") => {
    switch (command) {
      case "in":
        change(zoomBy(latest.current, ZOOM_STEP, middle()));
        break;
      case "out":
        change(zoomBy(latest.current, 1 / ZOOM_STEP, middle()));
        break;
      case "actual":
        change(zoomAt(latest.current, 1, middle()));
        break;
      case "fit":
        fit();
        break;
    }
  };

  // The wheel is not passive, so it moves the canvas rather than the page.
  const wheel = useRef<(event: WheelEvent) => void>(() => undefined);

  useEffect(() => {
    wheel.current = (event: WheelEvent) => {
      const element = root.current;

      if (element === null || size === null) {
        return;
      }

      event.preventDefault();

      const bounds = element.getBoundingClientRect();

      change(
        wheelChange(
          latest.current,
          event,
          { x: event.clientX - bounds.left, y: event.clientY - bounds.top },
          size,
        ),
      );
    };
  });

  useEffect(() => {
    const element = root.current;

    if (element === null) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      wheel.current(event);
    };

    element.addEventListener("wheel", onWheel, { passive: false });

    return () => {
      element.removeEventListener("wheel", onWheel);
    };
  }, []);

  const pointAt = (event: PointerEvent): ViewPoint => {
    const bounds = event.currentTarget.getBoundingClientRect();

    return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    const target = event.target as Element;

    if (
      event.button > 1 ||
      (target !== event.currentTarget && target.closest(notPanned) !== null)
    ) {
      return;
    }

    const point = pointAt(event);
    const points = new Map(gesture.current?.points ?? []);

    points.set(event.pointerId, point);
    event.currentTarget.setPointerCapture(event.pointerId);
    // A second finger starts the gesture over, as a pinch from where both are.
    gesture.current = { view: latest.current, points, start: new Map(points) };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;

    if (!current?.points.has(event.pointerId)) {
      return;
    }

    current.points.set(event.pointerId, pointAt(event));

    const ids = [...current.start.keys()].slice(0, 2);
    const [first, second] = ids.map((id) => ({
      from: current.start.get(id) ?? { x: 0, y: 0 },
      to: current.points.get(id) ?? { x: 0, y: 0 },
    }));

    if (first !== undefined && second !== undefined) {
      change(pinchChange(current.view, [first.from, second.from], [first.to, second.to]));
    } else if (first !== undefined) {
      change(panBy(current.view, first.to.x - first.from.x, first.to.y - first.from.y));
    }
  };

  const onPointerEnd = (event: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;

    if (current === null) {
      return;
    }

    current.points.delete(event.pointerId);
    gesture.current =
      current.points.size === 0
        ? null
        : { view: latest.current, points: current.points, start: new Map(current.points) };
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const command = zoomKey(event);

    if (command === null || isTextField(event.target)) {
      return;
    }

    event.preventDefault();
    zoom(command);
  };

  const percent = new Intl.NumberFormat(formattingLocale(), {
    style: "percent",
    maximumFractionDigits: 0,
  }).format(view.scale);
  const grid = 20 * view.scale;

  return (
    <div
      ref={root}
      role="group"
      aria-label={label}
      aria-keyshortcuts="+ - 0 1"
      {...(tabbable ? { tabIndex: 0 } : {})}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerEnd}
      onPointerCancel={onPointerEnd}
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

      <div
        data-no-pan
        className="absolute bottom-3 left-3 flex h-8 items-stretch overflow-hidden rounded-key bg-raised type-small text-ink shadow-[inset_0_0_0_1px_var(--color-line)]"
      >
        <Button
          aria-label={t`Zoom out`}
          className={controlClass}
          onPress={() => {
            zoom("out");
          }}
        >
          <IconMinus aria-hidden="true" size={14} stroke={2} />
        </Button>
        <span
          aria-live="polite"
          className="flex min-w-14 items-center justify-center border-x border-line-soft px-2 type-data"
        >
          {percent}
        </span>
        <Button
          aria-label={t`Zoom in`}
          className={controlClass}
          onPress={() => {
            zoom("in");
          }}
        >
          <IconPlus aria-hidden="true" size={14} stroke={2} />
        </Button>
        <Button
          className={cx(controlClass, "border-l border-line-soft px-3 font-semibold")}
          onPress={fit}
        >
          {t`Fit`}
        </Button>
        {controls}
      </div>

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
