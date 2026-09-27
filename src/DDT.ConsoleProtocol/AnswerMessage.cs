// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The console's answer to the open question Id. The agent checks every answer itself, so a question may come again,
// with its Error saying why.
public sealed record AnswerMessage(int Id, ConsoleAnswer Answer) : ConsoleMessage;
