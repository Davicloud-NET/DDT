// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconMinus, IconPlus } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button } from "react-aria-components";

import { formattingLocale } from "@/i18n/i18n";

import { cx } from "./cx";
import type { ZoomCommand } from "./viewTransform";

const controlClass =
  "flex h-full min-w-8 cursor-pointer items-center justify-center px-2 text-ink motion-colors outline-none " +
  "hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:-outline-offset-2 " +
  "focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45";

// The zoom buttons in a canvas's corner, with the scale between them and the page's own buttons after them.
export function ZoomControls({
  scale,
  onZoom,
  children,
}: {
  scale: number;
  onZoom: (command: ZoomCommand) => void;
  children?: ReactNode;
}) {
  const { t } = useLingui();
  const percent = new Intl.NumberFormat(formattingLocale(), {
    style: "percent",
    maximumFractionDigits: 0,
  }).format(scale);

  return (
    <div
      data-no-pan
      className="absolute bottom-3 left-3 flex h-8 items-stretch overflow-hidden rounded-key bg-raised type-small text-ink shadow-[inset_0_0_0_1px_var(--color-line)]"
    >
      <Button
        aria-label={t`Zoom out`}
        className={controlClass}
        onPress={() => {
          onZoom("out");
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
          onZoom("in");
        }}
      >
        <IconPlus aria-hidden="true" size={14} stroke={2} />
      </Button>
      <Button
        className={cx(controlClass, "border-l border-line-soft px-3 font-semibold")}
        onPress={() => {
          onZoom("fit");
        }}
      >
        {t`Fit`}
      </Button>
      {children}
    </div>
  );
}
