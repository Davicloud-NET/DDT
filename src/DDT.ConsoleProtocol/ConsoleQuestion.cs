// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.ConsoleProtocol;

// Something the agent asks the person at the machine. Each kind says which part of a ConsoleAnswer it takes. A question
// whose answer the agent refuses comes again. If it has an Error, that says why, in the words the agent logged.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SignInQuestion), "signIn")]
[JsonDerivedType(typeof(SequenceQuestion), "sequence")]
[JsonDerivedType(typeof(DiskQuestion), "disk")]
[JsonDerivedType(typeof(ComputerNameQuestion), "computerName")]
[JsonDerivedType(typeof(EraseQuestion), "erase")]
[JsonDerivedType(typeof(SecureBootQuestion), "secureBoot")]
[JsonDerivedType(typeof(InputsQuestion), "inputs")]
[JsonDerivedType(typeof(PauseQuestion), "pause")]
public abstract record ConsoleQuestion;
