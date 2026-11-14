// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// One Windows package per language, named "<language> language pack <suffix>".
public sealed record ImportLanguagesRequest(string Path, IReadOnlyList<string> Languages, string? Suffix = null);
