// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Types for the web project's tests, which import the generator to check the catalog it wrote.
export const serverMessagesPath: string;
export const webCatalogPath: string;
export function readServerMessages(): Record<string, string>;
export function renderWebCatalog(messages: Record<string, string>): string;
