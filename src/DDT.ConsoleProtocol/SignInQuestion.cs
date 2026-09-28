// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// One field of the sign-in that authorizes the machine: user name, password, then the authenticator code if the account
// has one. Answer with Text; an empty password or code goes back a field. The password is typed where nobody sees it.
public sealed record SignInQuestion(SignInField Field, string? UserName, string? Error) : ConsoleQuestion;
