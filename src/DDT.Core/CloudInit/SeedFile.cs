// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.CloudInit;

// A file on the seed beside cloud-init's own. Path separates folders with /.
public sealed record SeedFile(string Path, byte[] Content);
