// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Legal;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

public sealed class LegalDocument(Localizer localizer, LegalTexts.Document document) : ObservableObject
{
    public LegalTexts.Document Document => document;

    public string Title => LegalTexts.Title(localizer, document);

    public string File => document.File;

    public void Refresh() => Raise(nameof(Title));
}
