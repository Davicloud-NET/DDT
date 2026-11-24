// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { LanguageIsoDialog } from "./languages/LanguageIsoDialog";
import { LanguageIsoPanel } from "./languages/LanguageIsoPanel";
import { PackagesPage } from "./PackagesPage";

// Updates, language packs and features on demand. Languages come from the Languages and Optional Features ISO, read
// on the server.
export function WindowsPackagesPage() {
  const [iso, setIso] = useState<string | null>(null);

  return (
    <>
      <PackagesPage
        kind="WindowsPackage"
        onLanguageIso={(file) => {
          setIso(file.path);
        }}
      >
        <LanguageIsoPanel onChoose={setIso} />
      </PackagesPage>
      <LanguageIsoDialog
        path={iso}
        onClose={() => {
          setIso(null);
        }}
      />
    </>
  );
}
