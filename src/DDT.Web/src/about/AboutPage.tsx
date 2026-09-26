// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { msg } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";

import { StandaloneHeader } from "@/app/StandaloneHeader";
import { aboutQuery, ATTRIBUTION, legalDocumentUrl, SOURCE_URL } from "@/about/about";
import { Notice } from "@/ui/Notice";

const notices = [
  { name: "LICENSE", description: msg`The GNU General Public License, version 3` },
  {
    name: "NOTICE",
    description: msg`The attribution notice, the warranty disclaimer and the additional terms`,
  },
  {
    name: "THIRD-PARTY-NOTICES.md",
    description: msg`The software by others that DDT contains, and its licences`,
  },
  {
    name: "licenses/web/THIRD-PARTY-LICENSES.txt",
    description: msg`The licences of the packages in this web interface`,
  },
];

function Section({ title, children }: { title: ReactNode; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2 border-t border-line-soft pt-4">
      <h2 className="type-heading">{title}</h2>
      {children}
    </section>
  );
}

const linkClass = "text-ink underline underline-offset-3 hover:text-run-text";

// GPLv3 section 0 asks an interactive interface to show these notices, so they are part of the page and do not
// depend on the server answering. The attribution line stays in English: NOTICE's section 7(b) term asks for it
// word for word. The server adds its version and the licence texts it carries.
export function AboutPage() {
  const { i18n } = useLingui();
  const about = useQuery(aboutQuery);
  const version = about.data?.version;
  const licenceTexts =
    about.data?.legalDocuments.filter((name) => name.startsWith("licenses/")) ?? [];

  return (
    <div className="flex min-h-full flex-col">
      <StandaloneHeader />
      <main className="flex justify-center px-4 py-10">
        <article className="flex w-full max-w-180 flex-col gap-5 rounded-panel bg-panel p-8 shadow-panel">
          <h1 className="type-title">
            <Trans>About DDT</Trans>
          </h1>
          <div className="flex flex-col gap-1">
            <p className="type-label">{ATTRIBUTION}</p>
            {version !== undefined ? (
              <p className="type-small text-muted">
                <Trans>Version {version}</Trans>
              </p>
            ) : null}
          </div>
          <p className="max-w-[70ch]">
            <Trans>
              DDT is free software: you can redistribute it and modify it under the terms of the GNU
              General Public License as published by the Free Software Foundation, either version 3
              of the License or, at your option, any later version, together with additional terms
              under section 7 of that licence.
            </Trans>
          </p>
          <p className="max-w-[70ch]">
            <Trans>
              DDT comes with ABSOLUTELY NO WARRANTY, to the extent permitted by applicable law. See
              the GNU General Public License for details. DDT contains software by others, under
              their own licences.
            </Trans>
          </p>

          <Section title={<Trans>Licence and notices</Trans>}>
            <ul className="flex flex-col gap-1.5">
              {notices.map((notice) => (
                <li key={notice.name} className="flex flex-col">
                  <a href={legalDocumentUrl(notice.name)} className={`${linkClass} type-data`}>
                    {notice.name}
                  </a>
                  <span className="type-small text-muted">{i18n._(notice.description)}</span>
                </li>
              ))}
            </ul>
          </Section>

          <Section title={<Trans>Source code</Trans>}>
            <a href={SOURCE_URL} className={`${linkClass} type-data`}>
              {SOURCE_URL}
            </a>
          </Section>

          <Section title={<Trans>Licence texts of the software by others</Trans>}>
            {about.isError ? (
              <Notice tone="fail">
                <Trans>The version and the licence texts could not be loaded.</Trans>
              </Notice>
            ) : null}
            {licenceTexts.length > 0 ? (
              <ul className="flex flex-col gap-1">
                {licenceTexts.map((name) => (
                  <li key={name}>
                    <a href={legalDocumentUrl(name)} className={`${linkClass} type-data`}>
                      {name}
                    </a>
                  </li>
                ))}
              </ul>
            ) : null}
          </Section>

          <Link to="/machines" className={`${linkClass} self-start type-label`}>
            <Trans>Back to DDT</Trans>
          </Link>
        </article>
      </main>
    </div>
  );
}
