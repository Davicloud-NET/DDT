// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The Secure Boot override: the disk image would not start with the Secure Boot this machine has on.
public sealed class SecureBootViewModel : TypedWordViewModel
{
    private readonly MicrosoftUefiCas? _trusted;

    public SecureBootViewModel(
        Localizer localizer,
        int id,
        SecureBootQuestion question,
        MicrosoftUefiCas? trusted,
        Action<int, ConsoleAnswer> answer)
        : base(localizer, id, question?.Word ?? throw new ArgumentNullException(nameof(question)), answer)
    {
        Question = question;
        _trusted = trusted;
    }

    public SecureBootQuestion Question { get; }

    public string Title => T("Write it despite Secure Boot?");

    public Tag Tag => Tag.Of(T("Secure Boot"), TagTone.Attention);

    public string Problem => Say.SecureBootProblem(L, Question);

    public IReadOnlyList<Fact> Facts
    {
        get
        {
            List<Fact> facts = [new(T("Task sequence"), Question.SequenceName)];

            if (Question.ImageName is { } image)
            {
                facts.Add(new Fact(T("Disk image"), image));
            }

            if (Question.Problem == SecureBootProblem.UntrustedCa)
            {
                facts.Add(new Fact(T("Signed under"), Say.Cas(L, Question.SignedUnder)));
            }

            facts.Add(new Fact(T("This machine trusts"), Say.Cas(L, _trusted)));

            return facts;
        }
    }

    public string Label => F("Type {word} to write it all the same", ("word", Word));

    public string SubmitLabel => T("Write it anyway");

    public string BackLabel => T("Back, write nothing");
}
