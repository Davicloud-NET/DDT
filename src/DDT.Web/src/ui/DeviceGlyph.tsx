// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  IconBox,
  IconDeviceDesktop,
  IconDeviceLaptop,
  IconDeviceTablet,
  IconServer,
  IconDeviceUnknown,
  type Icon,
} from "@tabler/icons-react";

import { cx } from "./cx";

// What kind of computer a machine is, so a list shows the hardware and not only names. It comes from the SMBIOS
// chassis type the agent reads; "unknown" until an agent reports it.
export type DeviceKind = "laptop" | "desktop" | "tablet" | "server" | "virtual" | "unknown";

const glyphs: Record<DeviceKind, Icon> = {
  laptop: IconDeviceLaptop,
  desktop: IconDeviceDesktop,
  tablet: IconDeviceTablet,
  server: IconServer,
  virtual: IconBox,
  unknown: IconDeviceUnknown,
};

// The glyph sits in a well, one shade below the panel, like a part mounted in the surface.
export function DeviceGlyph({
  kind,
  size = "md",
  className,
}: {
  kind: DeviceKind;
  size?: "md" | "lg";
  className?: string;
}) {
  const Glyph = glyphs[kind];
  const large = size === "lg";

  return (
    <span
      aria-hidden="true"
      className={cx(
        "flex shrink-0 items-center justify-center rounded-key bg-well text-ink-2 shadow-[inset_0_0_0_1px_var(--color-line-soft)]",
        large ? "size-18" : "size-9.5",
        className,
      )}
    >
      <Glyph size={large ? 44 : 24} stroke={1.4} />
    </span>
  );
}
