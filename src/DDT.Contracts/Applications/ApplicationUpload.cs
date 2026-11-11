// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// The answer to a completed application upload. Suggestion is null when no known installer was found.
public sealed record ApplicationUpload(ApplicationFiles Files, ApplicationSuggestion? Suggestion);
