// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// A folder on the server that images are imported from. Default is the one in the store, which every server has.
public sealed record ImportFolder(string Path, bool Default);
