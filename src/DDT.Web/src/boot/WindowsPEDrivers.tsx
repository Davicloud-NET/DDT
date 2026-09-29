// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { formatBytes } from "@/lib/format";
import { EmptyState } from "@/ui/EmptyState";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";

import type { BootImageView } from "./bootImage";

// The driver packages flagged for Windows PE, each with whether the last build put it into the image.
export function WindowsPEDrivers({ view }: { view: BootImageView }) {
  const built = new Set(
    view.build?.drivers.map((driver) => `${driver.packageId} ${driver.sha256}`),
  );

  return (
    <Panel title={<Trans>Drivers for Windows PE</Trans>} flush>
      {view.drivers.length === 0 ? (
        <EmptyState title={<Trans>No drivers flagged</Trans>}>
          <Trans>
            The boot image has Windows PE's own drivers only. Flag a driver package under{" "}
            <Link to="/library/drivers" className="underline">
              Library, Drivers
            </Link>{" "}
            when a machine cannot reach the network or see its disk in Windows PE.
          </Trans>
        </EmptyState>
      ) : (
        <ul className="flex flex-col">
          {view.drivers.map((driver) => (
            <li
              key={driver.packageId}
              className="flex items-center gap-3 border-b border-line-soft px-4 py-2.5 last:border-b-0"
            >
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate type-label text-ink">{driver.name}</span>
                <span className="truncate type-data text-muted">{driver.sha256.slice(0, 16)}</span>
              </span>
              <span className="type-small text-muted">{formatBytes(driver.sizeBytes)}</span>
              {built.has(`${driver.packageId} ${driver.sha256}`) ? (
                <StateTag tone="ok">
                  <Trans>In the image</Trans>
                </StateTag>
              ) : (
                <StateTag tone="attention">
                  <Trans>Not built yet</Trans>
                </StateTag>
              )}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
