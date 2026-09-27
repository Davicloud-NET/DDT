// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// One field of the sign-in with a DDT account, which authorizes the machine: the user name, then the password for
// UserName, then, for an account with an authenticator, the code. Answer with Text. An empty password goes back to the
// user name, to use another account, and an empty code back to the password. Error is what went wrong with the last
// attempt, such as a wrong password or a locked account. The password is typed where nobody else sees it.
public sealed record SignInQuestion(SignInField Field, string? UserName, string? Error) : ConsoleQuestion;
