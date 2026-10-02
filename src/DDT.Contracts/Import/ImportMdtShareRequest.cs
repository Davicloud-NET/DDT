// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// ImageFiles are MdtImageFile.File and DriverGroups MdtDriverGroup.Id, as the share's view gave them.
public sealed record ImportMdtShareRequest(string Path, IReadOnlyList<string> ImageFiles, IReadOnlyList<string> DriverGroups);
