// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Types for the web project's tests, which import the generator to check the theme it wrote.
export const tokensPath: string;
export const webThemePath: string;
export function readTokens(): unknown;
export function contrast(foreground: string, background: string): number;
export function contrastProblems(tokens: unknown): string[];
export function renderWebTheme(tokens: unknown): string;
