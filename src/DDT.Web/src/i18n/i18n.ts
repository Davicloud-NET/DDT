// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type Messages } from "@lingui/core";

// The languages a user can pick, each named in its own language. "pseudo" is for development only: stretched,
// accented English that shows text nobody marked for translation and layouts too tight for German.
export const LANGUAGES = { en: "English", de: "Deutsch" } as const;

export type Language = keyof typeof LANGUAGES | "pseudo";

const STORAGE_KEY = "ddt.language";

const catalogs: Record<Language, () => Promise<{ messages: Messages }>> = {
  en: () => import("../locales/en/messages.po"),
  de: () => import("../locales/de/messages.po"),
  pseudo: () => import("../locales/pseudo/messages.po"),
};

function isLanguage(value: unknown): value is Language {
  return typeof value === "string" && value in catalogs;
}

function stored(): string | null {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

// The user's choice, else the first browser language DDT has, else English.
export function preferredLanguage(): Language {
  const choice = stored();

  if (isLanguage(choice)) {
    return choice;
  }

  const browser = navigator.languages
    .map((tag) => tag.split("-")[0]?.toLowerCase())
    .find(isLanguage);

  return browser ?? "en";
}

export async function activateLanguage(language: Language): Promise<void> {
  const { messages } = await catalogs[language]();

  i18n.loadAndActivate({ locale: language, messages });
  document.documentElement.lang = language === "pseudo" ? "en" : language;
}

export async function chooseLanguage(language: Language): Promise<void> {
  try {
    window.localStorage.setItem(STORAGE_KEY, language);
  } catch {
    // Without storage the choice lasts until the page is reloaded.
  }

  await activateLanguage(language);
}

// The locale for Intl formatting of dates, numbers and sizes.
export function formattingLocale(): string {
  return i18n.locale === "pseudo" || i18n.locale === "" ? "en" : i18n.locale;
}
