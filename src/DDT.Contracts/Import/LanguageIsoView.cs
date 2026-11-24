// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// The languages a Languages and Optional Features ISO has a language pack for, and a suggested end for the names.
public sealed record LanguageIsoView(string Path, IReadOnlyList<string> Languages, string Suffix);
