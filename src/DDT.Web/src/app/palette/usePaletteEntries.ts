// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { imagesQuery } from "@/images/images";
import { formatMac, machinesQuery } from "@/machines/machines";
import { displayName, hardwareLine } from "@/machines/machineView";
import { sequencesQuery } from "@/sequences/sequences";

import { categories } from "../navigation";

export interface PaletteEntry {
  id: string;
  to: string;
  // Params and search for a page with a parameter, such as a machine's.
  params?: Record<string, string>;
  label: string;
  detail?: string;
  // Words that find the entry besides its label, such as a machine's MAC addresses.
  keywords?: string;
}

// What the palette lists, from the lists the pages already hold: every page, machine, task sequence and image.
export function usePaletteEntries() {
  const { i18n } = useLingui();
  const machines = useQuery(machinesQuery).data ?? [];
  const sequences = useQuery(sequencesQuery).data ?? [];
  const images = useQuery(imagesQuery).data ?? [];

  const pages: PaletteEntry[] = categories.flatMap((category) =>
    category.pages.map((page) => ({
      id: `page:${page.to}`,
      to: page.to,
      label: i18n._(page.label),
      detail: i18n._(category.label),
    })),
  );
  const machineEntries: PaletteEntry[] = machines.map((machine) => ({
    id: `machine:${machine.id}`,
    to: "/machines/$machineId",
    params: { machineId: machine.id },
    label: displayName(machine),
    detail: hardwareLine(machine),
    keywords: [
      machine.serialNumber,
      machine.lastSeenAddress,
      ...machine.macAddresses.flatMap((mac) => [mac, formatMac(mac)]),
    ]
      .filter((value): value is string => value !== null)
      .join(" "),
  }));
  const sequenceEntries: PaletteEntry[] = sequences.map((sequence) => ({
    id: `sequence:${sequence.id}`,
    to: "/deployment/sequences/$sequenceId",
    params: { sequenceId: sequence.id },
    label: sequence.name,
  }));
  const imageEntries: PaletteEntry[] = images.map((image) => ({
    id: `image:${image.id}`,
    to: "/library/images",
    label: image.name,
    ...(image.originalFileName === null ? {} : { detail: image.originalFileName }),
  }));
  const all = [...pages, ...machineEntries, ...sequenceEntries, ...imageEntries];

  return { pages, machineEntries, sequenceEntries, imageEntries, all };
}
