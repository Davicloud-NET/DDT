// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.WindowsPackages;

public enum WindowsPackageFileKind
{
    // A .msu: a cumulative update or one of its checkpoints.
    Update,
    LanguagePack,

    // Microsoft-Windows-LanguageFeatures-*: Basic, Handwriting, OCR, Speech, TextToSpeech, the fonts.
    LanguageFeature,

    // Any other .cab: a feature on demand, or a language's part of one.
    Feature,
}
