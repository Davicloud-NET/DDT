// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Legal;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The console's Appropriate Legal Notices: DDT's attribution notice, which NOTICE requires word for word, what the GPL
// allows, the software by others the console contains, and every licence text it carries.
public sealed class LicencesViewModel : OverlayViewModel
{
    private LegalDocument _selected;
    private IReadOnlyList<string>? _lines;

    public LicencesViewModel(Localizer localizer)
        : base(localizer)
    {
        Documents = [.. LegalTexts.Documents.Select(document => new LegalDocument(localizer, document))];
        _selected = Documents[0];
    }

    public string Title => T("Licences");

    public override string CloseLabel => T("Close the licences");

    // Never translated: additional term 1 in NOTICE requires this text.
    public string Attribution => LegalTexts.AttributionNotice;

    public string Warranty => T("This program comes with ABSOLUTELY NO WARRANTY.");

    public string Licence => T(
        "It is free software: you can redistribute it and modify it under the GNU General Public License, version 3 or " +
        "later, with additional terms. This view holds the texts.");

    public string OthersTitle => T("Software by others in this console");

    public IReadOnlyList<Fact> Others =>
    [
        new("Avalonia", F("{holder}, {licence}", ("holder", "AvaloniaUI OÜ"), ("licence", T("MIT licence")))),
        new("MicroCom", F("{holder}, {licence}", ("holder", "Nikita Tsukanov"), ("licence", T("MIT licence")))),
        new("SkiaSharp, HarfBuzzSharp", F("{holder}, {licence}", ("holder", "Xamarin, Inc., Microsoft Corporation"), ("licence", T("MIT licence")))),
        new("Skia", F("{holder}, {licence}", ("holder", "Google Inc."), ("licence", T("BSD licence")))),
        new("HarfBuzz", F("{holder}, {licence}", ("holder", T("the HarfBuzz authors")), ("licence", T("Old MIT licence")))),
        new(".NET", F("{holder}, {licence}", ("holder", ".NET Foundation and Contributors"), ("licence", T("MIT licence")))),
        new("Archivo", F("{holder}, {licence}", ("holder", "The Archivo Project Authors"), ("licence", T("SIL Open Font License 1.1")))),
        new("Martian Mono", F("{holder}, {licence}", ("holder", "The Martian Mono Project Authors"), ("licence", T("SIL Open Font License 1.1")))),
    ];

    public string DocumentsTitle => T("Licence texts");

    public string DocumentsHint => T("Up and Down choose a text.");

    public IReadOnlyList<LegalDocument> Documents { get; }

    public LegalDocument Selected
    {
        get => _selected;
        set
        {
            if (value is not null && Set(ref _selected, value))
            {
                _lines = null;
                Raise(nameof(Lines));
            }
        }
    }

    // The selected text a paragraph at a time, so a long one scrolls without laying out all of it.
    public IReadOnlyList<string> Lines => _lines ??= LegalTexts.Paragraphs(LegalTexts.Read(Selected.Document.File));

    public override void Refresh()
    {
        foreach (LegalDocument document in Documents)
        {
            document.Refresh();
        }

        base.Refresh();
    }
}

public sealed class LegalDocument(Localizer localizer, LegalTexts.Document document) : ObservableObject
{
    public LegalTexts.Document Document => document;

    public string Title => LegalTexts.Title(localizer, document);

    public string File => document.File;

    public void Refresh() => Raise(nameof(Title));
}
