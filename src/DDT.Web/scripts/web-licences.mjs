// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Writes licenses/web/THIRD-PARTY-LICENSES.txt: the licence text of every package in the non-development closure
// of package-lock.json, plus the build tools whose own code ends up in the bundle. ThirdPartyNoticesTests checks
// the file against the lock file, so run `npm run licences` after every change to the dependencies.
//
// A package's text comes from the licence file npm installed with it. A package without one keeps the entry the
// file already has for it, because that entry says where its text came from; the script stops when there is none,
// so a person writes it. The build tools' entries are hand-written the same way.

import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const web = fileURLToPath(new URL("..", import.meta.url));
const target = fileURLToPath(
    new URL("../../../licenses/web/THIRD-PARTY-LICENSES.txt", import.meta.url),
);
const bundledBuildTools = ["rolldown", "vite", "tailwindcss"];
const separator = "=".repeat(100);
const rule = "-".repeat(100);

const preamble = `Third-party licences in the DDT web UI bundle

Vite builds the DDT web UI (src/DDT.Web) into the static files the server serves from wwwroot.
This file holds the licence of every npm package in the non-development closure of
src/DDT.Web/package-lock.json, followed by the build tools whose own code the bundle contains.
The build leaves many of these packages out of the bundle entirely, for example the Node.js
dependencies of @microsoft/signalr and the Babel packages Lingui's runtime declares for its
macros, so the list is a superset of what the bundle holds.

Each licence text is copied from the licence file in the package as npm installs it. Where a
package has no licence file, its entry says so, and says where its text comes from.
`;

function lockPackages() {
    const lock = JSON.parse(readFileSync(join(web, "package-lock.json"), "utf8"));

    return Object.entries(lock.packages)
        .filter(([path]) => path.length > 0)
        .map(([path, entry]) => ({
            name: path.split("node_modules/").at(-1),
            path: join(web, path),
            version: entry.version,
            licence: entry.license ?? "unknown",
            dev: entry.dev === true,
        }))
        .filter((entry) => !entry.dev || bundledBuildTools.includes(entry.name));
}

function licenceFile(directory) {
    if (!existsSync(directory)) {
        return null;
    }

    const name = readdirSync(directory).find((file) =>
        /^(licen[cs]e|copying)([-_][a-z0-9-]+)?(\.(md|txt|markdown))?$/i.test(file),
    );

    return name
        ? readFileSync(join(directory, name), "utf8").replace(/\r\n/g, "\n").trimEnd()
        : null;
}

// The entries the file has now, by "name version" and by name, to keep hand-written ones.
function existingEntries() {
    if (!existsSync(target)) {
        return new Map();
    }

    const entries = new Map();
    const blocks = readFileSync(target, "utf8")
        .replace(/\r\n/g, "\n")
        .split(`${separator}\n`)
        .slice(1);

    for (const block of blocks) {
        const [heading, ...body] = block.split("\n");
        const name = heading.slice(0, heading.lastIndexOf(" "));

        entries.set(heading, body.join("\n").trimEnd());
        entries.set(name, body.join("\n").trimEnd());
    }

    return entries;
}

function main() {
    const existing = existingEntries();
    const missing = [];
    const packages = lockPackages().sort(
        (a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version),
    );
    const blocks = [];

    for (const entry of packages) {
        const heading = `${entry.name} ${entry.version}`;
        const text = bundledBuildTools.includes(entry.name) ? null : licenceFile(entry.path);

        if (text !== null) {
            blocks.push(
                `${separator}\n${heading}\nLicence: ${entry.licence}\n${rule}\n\n${text}\n`,
            );
        } else if (existing.has(heading)) {
            blocks.push(`${separator}\n${heading}\n${existing.get(heading)}\n`);
        } else {
            missing.push(heading);
        }
    }

    if (missing.length > 0) {
        console.error(
            `These packages have no licence file and no entry yet; write one in ${target} and run again:\n` +
                missing.map((heading) => `  ${heading}`).join("\n"),
        );
        process.exit(1);
    }

    writeFileSync(target, `${preamble}\n${blocks.join("\n")}`, "utf8");
    console.log(`Wrote ${packages.length} entries to ${target}`);
}

main();
