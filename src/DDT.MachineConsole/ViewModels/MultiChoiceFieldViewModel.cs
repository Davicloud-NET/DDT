// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Any number of options. A few are a row of keys that each toggle on and off; a long list is a search over a list of
// rows, where a row the search hides stays ticked. The answer lists the chosen values in the order shown.
public sealed class MultiChoiceFieldViewModel : InputFieldViewModel
{
    // A list of more choices than this gets a search.
    public const int SearchAbove = 8;

    private string _search = string.Empty;
    private IReadOnlyList<ChoiceItem> _shown;
    private ChoiceItem? _highlighted;

    public MultiChoiceFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
        : base(localizer, input, changed)
    {
        HashSet<string> chosen = [.. (input.Default ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        Items =
        [
            .. input.Choices.Select(choice => new ChoiceItem(choice.Value, () => choice.Label ?? choice.Value, Ticked)
            {
                IsChosen = chosen.Contains(choice.Value),
            }),
        ];
        _shown = Items;
    }

    public IReadOnlyList<ChoiceItem> Items { get; }

    public bool IsSearchable => Items.Count > SearchAbove;

    // The keys of a short list. A long one has none.
    public IReadOnlyList<ChoiceItem> Keys => IsSearchable ? [] : Items;

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                Filter();
            }
        }
    }

    // The rows the search leaves.
    public IReadOnlyList<ChoiceItem> Shown => _shown;

    // The row Space ticks.
    public ChoiceItem? Highlighted
    {
        get => _highlighted;
        set => Set(ref _highlighted, value);
    }

    public bool HasNoMatch => IsSearchable && _shown.Count == 0;

    public string SearchHint => L.T("Search");

    public string NoMatchText => L.T("No choice matches.");

    public string ChosenText =>
        L.F("{count} chosen", ("count", Items.Count(item => item.IsChosen).ToString(CultureInfo.CurrentCulture)));

    public override bool IsAnswered => IsOptional || Items.Any(item => item.IsChosen);

    public override ConsoleInputValue Value() =>
        new(Input.Name, string.Join(';', Items.Where(item => item.IsChosen).Select(item => item.Value)));

    // Moves the highlight through the rows shown, from none to the first.
    public void Move(int by)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        int at = _highlighted is null ? -1 : IndexOf(_highlighted);
        Highlighted = _shown[Math.Clamp(at + by, 0, _shown.Count - 1)];
    }

    public void TickHighlighted()
    {
        if (_highlighted is { } item)
        {
            item.IsChosen = !item.IsChosen;
        }
    }

    public override void Refresh()
    {
        foreach (ChoiceItem item in Items)
        {
            item.Refresh();
        }

        base.Refresh();
    }

    private int IndexOf(ChoiceItem item)
    {
        for (int index = 0; index < _shown.Count; index++)
        {
            if (ReferenceEquals(_shown[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private void Filter()
    {
        string wanted = _search.Trim();
        _shown = [.. Items.Where(item => item.Label.Contains(wanted, StringComparison.CurrentCultureIgnoreCase))];

        if (_highlighted is not null && IndexOf(_highlighted) < 0)
        {
            Highlighted = null;
        }

        Raise(nameof(Shown));
        Raise(nameof(HasNoMatch));
    }

    private void Ticked()
    {
        Raise(nameof(ChosenText));
        Changed();
    }
}
