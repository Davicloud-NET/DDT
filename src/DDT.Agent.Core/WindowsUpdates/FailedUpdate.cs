// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// An update whose download or installation failed, with its last HRESULT.
public sealed record FailedUpdate(string Name, int Tries, int HResult);
