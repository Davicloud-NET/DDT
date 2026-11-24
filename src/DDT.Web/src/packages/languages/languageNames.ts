// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// A language tag in the reader's language, such as "German (Germany)" for de-DE. The tag itself where the browser
// knows no name.
export function languageName(tag: string, locale: string): string {
  try {
    return new Intl.DisplayNames([locale], { type: "language" }).of(tag) ?? tag;
  } catch {
    return tag;
  }
}

// The name a language's package gets on the server.
export function languagePackageName(tag: string, suffix: string): string {
  const end = suffix.trim();

  return end === "" ? `${tag} language pack` : `${tag} language pack ${end}`;
}
