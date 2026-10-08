// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

import { legalDocumentUrl } from "@/about/about";
import { serverText, type ServerMessage } from "@/lib/serverText";

// The Linux boot environment a release of DDT brings, as GET /api/boot-image describes it. problem says in English why
// no machine can be started in it, and problemText holds the same sentence as a code with its values.
export interface LinuxBootView {
  available: boolean;
  problem: string | null;
  problemText: ServerMessage | null;
  // The snapshot.debian.org time its packages are pinned at, such as 20261006T203214Z.
  snapshot: string | null;
  kernel: string | null;
  shim: string | null;
  grub: string | null;
  // The Microsoft third-party UEFI CAs its shim is signed under, as the server writes a flags value. Null when it
  // found none.
  shimSignedUnder: string | null;
  // The keyboard layouts its console has, by the names of their keymaps.
  keymaps: string[];
  // The boot file a machine that starts it loads.
  bootFile: string;
}

// Where every release publishes what the environment was built from.
export const linuxSourcesUrl = legalDocumentUrl("licenses/linux-boot/SOURCES.md");

// The file ProxyDHCP names when a server does not say.
export const linuxBootFile = "linux/shimx64.efi";

// The layout a machine has when the settings name none.
export const defaultKeymap = "us";

const keymapNames: Partial<Record<string, MessageDescriptor>> = {
  us: msg`English (United States)`,
  uk: msg`English (United Kingdom)`,
  de: msg`German`,
  ch: msg`German (Switzerland)`,
  fr: msg`French`,
  it: msg`Italian`,
  es: msg`Spanish`,
};

// A keymap's name in words, or the name itself for one this build does not know.
export function keymapName(keymap: string): string {
  const name = keymapNames[keymap];

  return name === undefined ? keymap : i18n._(name);
}

// Why no machine can be started in it, in the person's language. Null when one can.
export function linuxBootProblem(linux: LinuxBootView): string | null {
  return linux.problem === null
    ? null
    : serverText(linux.problemText?.code, linux.problemText?.args, linux.problem);
}

// "6 October 2026" from 20261006T203214Z, or the text as it is when it is no such time.
export function snapshotDate(snapshot: string, locale: string): string {
  const parts = /^(\d{4})(\d{2})(\d{2})T/.exec(snapshot);

  if (parts === null) {
    return snapshot;
  }

  const [, year = "", month = "", day = ""] = parts;

  return new Date(Date.UTC(Number(year), Number(month) - 1, Number(day))).toLocaleDateString(
    locale,
    { dateStyle: "long", timeZone: "UTC" },
  );
}
