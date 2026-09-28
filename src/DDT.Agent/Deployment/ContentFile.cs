// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A file to download, as the server announced it. Name is for messages, such as "package Dell drivers".
public sealed record ContentFile(string Name, string Sha256, long SizeBytes);
