// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// One field of a sequence's inputs. changed lets InputsViewModel check again whether all of them can be sent.
public abstract class InputFieldViewModel : ObservableObject
{
    private readonly Action _changed;

    protected InputFieldViewModel(Localizer localizer, ConsoleInput input, Action changed)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(changed);

        L = localizer;
        Input = input;
        _changed = changed;
    }

    public ConsoleInput Input { get; private set; }

    public string Label => Input.Label;

    public bool IsOptional => !Input.Required;

    public string OptionalLabel => L.T("Optional");

    public string? Help => string.IsNullOrWhiteSpace(Input.Help) ? null : Input.Help;

    public bool HasHelp => Help is not null;

    // The agent's error message about the previous answer.
    public string? Error => Input.Error;

    public bool HasError => !string.IsNullOrEmpty(Input.Error);

    // True if the field can be sent as it is, because it has an answer or it may stay empty.
    public abstract bool IsAnswered { get; }

    protected Localizer L { get; }

    public abstract ConsoleInputValue Value();

    // Whether this field can show the input asked again and keep what was typed. That needs the same kind and choices.
    public bool Takes(ConsoleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Kind == Input.Kind
            && input.Choices.Select(choice => choice.Value).SequenceEqual(Input.Choices.Select(choice => choice.Value), StringComparer.Ordinal);
    }

    // Shows the input asked again, with what was wrong with it. A password always has to be typed again.
    public InputFieldViewModel Asked(ConsoleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Input = input;
        ForgetSecrets();
        RaiseAll();

        return this;
    }

    // Once the answers are sent, anything that must not stay on the screen is cleared.
    public virtual void ForgetSecrets()
    {
    }

    public virtual void Refresh() => RaiseAll();

    protected void Changed() => _changed();
}
