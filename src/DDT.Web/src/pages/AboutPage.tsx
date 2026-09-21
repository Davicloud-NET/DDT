// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";

import { aboutQuery, ATTRIBUTION, legalDocumentUrl, SOURCE_URL } from "@/about/about";

import styles from "./AboutPage.module.scss";

const notices = [
  { name: "LICENSE", description: "the GNU General Public License, version 3" },
  {
    name: "NOTICE",
    description: "the attribution notice, the warranty disclaimer and the additional terms",
  },
  {
    name: "THIRD-PARTY-NOTICES.md",
    description: "the software by others that DDT contains, and its licences",
  },
  {
    name: "licenses/web/THIRD-PARTY-LICENSES.txt",
    description: "the licences of the packages in this web interface",
  },
] as const;

// GPLv3 section 0 asks an interactive interface to show these notices, so they are part of the page and do not
// depend on the server answering. The server adds its version and the licence texts it carries.
export function AboutPage() {
  const about = useQuery(aboutQuery);
  const licenceTexts =
    about.data?.legalDocuments.filter((name) => name.startsWith("licenses/")) ?? [];

  return (
    <div className={styles.page}>
      <article className={styles.card}>
        <h1>About DDT</h1>

        <p className={styles.attribution}>{ATTRIBUTION}</p>
        {about.isSuccess && <p className={styles.hint}>Version {about.data.version}</p>}

        <p>
          DDT is free software: you can redistribute it and modify it under the terms of the GNU
          General Public License as published by the Free Software Foundation, either version 3 of
          the License or, at your option, any later version, together with additional terms under
          section 7 of that licence.
        </p>
        <p>
          DDT comes with ABSOLUTELY NO WARRANTY, to the extent permitted by applicable law. See the
          GNU General Public License for details.
        </p>
        <p>DDT contains software by others, under their own licences.</p>

        <section className={styles.section}>
          <h2>Licence and notices</h2>
          <ul className={styles.list}>
            {notices.map((notice) => (
              <li key={notice.name}>
                <a href={legalDocumentUrl(notice.name)} className={styles.link}>
                  {notice.name}
                </a>
                : {notice.description}
              </li>
            ))}
          </ul>
        </section>

        <section className={styles.section}>
          <h2>Source code</h2>
          <p>
            <a href={SOURCE_URL} className={styles.link}>
              {SOURCE_URL}
            </a>
          </p>
        </section>

        <section className={styles.section}>
          <h2>Licence texts of the software by others</h2>
          {about.isError && (
            <p className={styles.error} role="alert">
              The version and the licence texts could not be loaded.
            </p>
          )}
          {licenceTexts.length > 0 && (
            <ul className={styles.list}>
              {licenceTexts.map((name) => (
                <li key={name}>
                  <a href={legalDocumentUrl(name)} className={styles.link}>
                    {name}
                  </a>
                </li>
              ))}
            </ul>
          )}
        </section>

        <Link to="/" className={styles.back}>
          Back to DDT
        </Link>
      </article>
    </div>
  );
}
