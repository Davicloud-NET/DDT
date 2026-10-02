// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Server.Import;

// One thing an import adds: an image file, or the folders of a driver group that become one driver package. Target is
// the model the package is for, where the group's name gave one.
public sealed record ImportItem(UploadKind Kind, string Name, string? File, IReadOnlyList<string> Folders, HardwareModel? Target = null, string? Description = null);
