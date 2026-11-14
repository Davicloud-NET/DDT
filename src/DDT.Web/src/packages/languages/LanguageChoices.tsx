// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Checkbox } from "@/ui/Checkbox";

import { languageName } from "./languageNames";

// A box per language, in two columns, named in the reader's language with the tag beside it.
export function LanguageChoices({
  languages,
  chosen,
  onToggle,
}: {
  languages: readonly string[];
  chosen: ReadonlySet<string>;
  onToggle: (language: string, ticked: boolean) => void;
}) {
  const { i18n } = useLingui();

  if (languages.length === 0) {
    return (
      <p className="type-small text-muted">
        <Trans>No language matches.</Trans>
      </p>
    );
  }

  return (
    <div className="grid max-h-80 grid-cols-1 gap-2 overflow-y-auto sm:grid-cols-2">
      {languages.map((language) => (
        <Checkbox
          key={language}
          isSelected={chosen.has(language)}
          onChange={(ticked) => {
            onToggle(language, ticked);
          }}
        >
          <span className="flex min-w-0 flex-col">
            <span>{languageName(language, i18n.locale)}</span>
            <span className="type-small text-muted">{language}</span>
          </span>
        </Checkbox>
      ))}
    </div>
  );
}
