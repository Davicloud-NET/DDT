// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// What the server can import: the files below its import folders, the MDT deployment shares among them, and the import
// that runs or ran last.
public sealed record ImportSources(
    IReadOnlyList<ImportFolder> Folders,
    IReadOnlyList<ImportFile> Files,
    IReadOnlyList<string> Shares,
    ImportStatus? Job);
