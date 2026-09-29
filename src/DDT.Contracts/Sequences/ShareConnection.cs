// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A share, such as \\files.corp.example\drivers, that DDT connects with Account while the step runs. Path is a
// template. Its host only comes from values fixed when the run starts, so a step can't send the account to a host of
// its choosing.
public sealed record ShareConnection(string Path, AccountReference Account);
