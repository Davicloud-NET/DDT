// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { importLanguages, languageIsoQuery } from "@/images/import/imports";

import { languageName } from "./languageNames";

// The languages ticked in an ISO, the search over them and the end of the package names. Closes once the import has
// started, which the page then follows.
export function useLanguageIsoForm(path: string | null, onClose: () => void) {
  const { i18n } = useLingui();
  const iso = useQuery({ ...languageIsoQuery(path ?? ""), enabled: path !== null });
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set());
  const [query, setQuery] = useState("");
  // Null until the operator changes it: the ISO's suggestion stands.
  const [suffix, setSuffix] = useState<string | null>(null);
  const view = path === null ? undefined : iso.data;
  const languages = view?.languages ?? [];
  const needle = query.trim().toLowerCase();
  const shown = languages.filter(
    (language) =>
      needle === "" ||
      language.toLowerCase().includes(needle) ||
      languageName(language, i18n.locale).toLowerCase().includes(needle),
  );
  const end = suffix ?? view?.suffix ?? "";

  const start = useMutation({
    mutationFn: () =>
      importLanguages(
        path ?? "",
        languages.filter((language) => chosen.has(language)),
        end,
      ),
    onSuccess: close,
  });

  function close() {
    setChosen(new Set());
    setQuery("");
    setSuffix(null);
    start.reset();
    onClose();
  }

  function toggle(language: string, ticked: boolean) {
    setChosen((current) => {
      const next = new Set(current);

      if (ticked) {
        next.add(language);
      } else {
        next.delete(language);
      }

      return next;
    });
  }

  return { iso, view, shown, chosen, toggle, query, setQuery, end, setSuffix, start, close };
}
