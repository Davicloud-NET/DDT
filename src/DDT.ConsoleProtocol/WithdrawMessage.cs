// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The agent no longer needs the answer to question Id, for example because an operator approved the machine on the web
// while the sign-in was open. The console closes the question. An answer already on its way is ignored.
public sealed record WithdrawMessage(int Id) : ConsoleMessage;
