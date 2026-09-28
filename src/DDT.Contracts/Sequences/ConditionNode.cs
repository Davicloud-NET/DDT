// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// A condition as a tree: groups of parts that must all, any or none hold, and tests at the leaves. A step's When, an
// IF's Test, a Repeat's Until and a rule's When are one. The discriminator values are stored, so they never change.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AllCondition), "all")]
[JsonDerivedType(typeof(AnyCondition), "any")]
[JsonDerivedType(typeof(NoneCondition), "none")]
[JsonDerivedType(typeof(TestCondition), "test")]
public abstract record ConditionNode;
