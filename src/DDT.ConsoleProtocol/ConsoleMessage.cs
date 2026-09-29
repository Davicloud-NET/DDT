// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.ConsoleProtocol;

// One message over the pipe between the agent and its console. The agent sends hello, refused, state, log, question and
// withdraw. The console sends hello and answer. Any change to a message needs a new HelloMessage.CurrentVersion.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(RefusedMessage), "refused")]
[JsonDerivedType(typeof(StateMessage), "state")]
[JsonDerivedType(typeof(LogMessage), "log")]
[JsonDerivedType(typeof(QuestionMessage), "question")]
[JsonDerivedType(typeof(WithdrawMessage), "withdraw")]
[JsonDerivedType(typeof(AnswerMessage), "answer")]
public abstract record ConsoleMessage;
