// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Applications;

// How the agent tells that an application is installed already, so it skips it. The discriminator values are stored,
// so they never change.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProductCodeDetection), "productCode")]
[JsonDerivedType(typeof(UninstallKeyDetection), "uninstallKey")]
[JsonDerivedType(typeof(FileDetection), "file")]
public abstract record ApplicationDetection;
