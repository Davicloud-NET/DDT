// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  IconArrowsSplit2,
  IconBraces,
  IconBuilding,
  IconCloudCode,
  IconCpu,
  IconDisc,
  IconDownload,
  IconFileText,
  IconLayoutColumns,
  IconLayoutGrid,
  IconPlayerPause,
  IconRepeat,
  IconRotateClockwise,
  IconTerminal2,
  type Icon,
} from "@tabler/icons-react";

import type { StepKind } from "@/sequences/sequences";

import { cx } from "./cx";

const glyphs: Record<StepKind, Icon> = {
  partition: IconLayoutColumns,
  applyImage: IconDownload,
  injectDrivers: IconCpu,
  writeUnattend: IconFileText,
  joinDomain: IconBuilding,
  runScript: IconTerminal2,
  reboot: IconRotateClockwise,
  writeRawImage: IconDisc,
  writeCloudInitSeed: IconCloudCode,
  setVariable: IconBraces,
  pause: IconPlayerPause,
  group: IconLayoutGrid,
  if: IconArrowsSplit2,
  repeat: IconRepeat,
};

export function NodeGlyph({ kind, className }: { kind: StepKind; className?: string }) {
  const Glyph = glyphs[kind];

  return <Glyph aria-hidden="true" size={18} stroke={1.75} className={cx("shrink-0", className)} />;
}
