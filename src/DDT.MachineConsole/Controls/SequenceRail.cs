// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Controls;

// The sequence rail, the signature of the run screen: one module per step in equal columns, with the step's number
// and name under it, and the phases the steps run in labelled above them. Where the columns get too narrow for names,
// only the numbers show; the heading above the rail names the running step anyway. A step that finishes or fails
// while the rail is on the screen flashes its column in its state's colour, faintly, fading over flash.
public sealed class SequenceRail : Panel
{
    public static readonly StyledProperty<IReadOnlyList<RailStep>?> StepsProperty =
        AvaloniaProperty.Register<SequenceRail, IReadOnlyList<RailStep>?>(nameof(Steps));

    public static readonly StyledProperty<IReadOnlyList<RailPhase>?> PhasesProperty =
        AvaloniaProperty.Register<SequenceRail, IReadOnlyList<RailPhase>?>(nameof(Phases));

    private const double Gap = 6;
    private const double RowGap = 8;
    private const double PhaseGap = 8;
    private const double ModuleHeight = 22;
    private const double NamesFrom = 64;
    private const double FlashStrength = 0.2;
    private const double FlashMargin = 4;

    private readonly List<StepParts> _steps = [];
    private readonly List<PhaseParts> _phases = [];

    static SequenceRail()
    {
        AffectsMeasure<SequenceRail>(StepsProperty, PhasesProperty);
    }

    public IReadOnlyList<RailStep>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public IReadOnlyList<RailPhase>? Phases
    {
        get => GetValue(PhasesProperty);
        set => SetValue(PhasesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StepsProperty || change.Property == PhasesProperty)
        {
            Rebuild();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        double column = ColumnWidth(width);
        bool names = column >= NamesFrom;
        double phaseHeight = 0;

        foreach (PhaseParts phase in _phases)
        {
            phase.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            phaseHeight = Math.Max(phaseHeight, phase.Label.DesiredSize.Height);
            phase.Line.Measure(new Size(width, 1));
        }

        double numberHeight = 0;
        double nameHeight = 0;

        foreach (StepParts step in _steps)
        {
            step.Flash.Measure(new Size(column + Gap, double.PositiveInfinity));
            step.Module.Measure(new Size(column, ModuleHeight));
            step.Number.Measure(new Size(column, double.PositiveInfinity));
            numberHeight = Math.Max(numberHeight, step.Number.DesiredSize.Height);
            step.Name.IsVisible = names;

            if (names)
            {
                step.Name.Measure(new Size(column, double.PositiveInfinity));
                nameHeight = Math.Max(nameHeight, step.Name.DesiredSize.Height);
            }
        }

        double height = (_phases.Count > 0 ? phaseHeight + 5 + PhaseGap : 0)
            + ModuleHeight + RowGap + numberHeight + (names ? RowGap + nameHeight : 0);

        return new Size(width, _steps.Count == 0 ? 0 : height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double column = ColumnWidth(finalSize.Width);
        double top = 0;

        if (_phases.Count > 0)
        {
            double labelHeight = _phases.Max(phase => phase.Label.DesiredSize.Height);
            int first = 0;

            foreach (PhaseParts phase in _phases)
            {
                double x = first * (column + Gap);
                double span = phase.Steps * column + (phase.Steps - 1) * Gap;
                phase.Label.Arrange(new Rect(x, 0, span, labelHeight));
                phase.Line.Arrange(new Rect(x, labelHeight + 4, span, 1));
                first += phase.Steps;
            }

            top = labelHeight + 5 + PhaseGap;
        }

        double numberTop = top + ModuleHeight + RowGap;
        double numberHeight = _steps.Count == 0 ? 0 : _steps.Max(step => step.Number.DesiredSize.Height);

        double bottom = numberTop + numberHeight;

        for (int index = 0; index < _steps.Count; index++)
        {
            StepParts step = _steps[index];
            double x = index * (column + Gap);
            step.Module.Arrange(new Rect(x, top, column, ModuleHeight));
            step.Number.Arrange(new Rect(x, numberTop, column, numberHeight));

            if (step.Name.IsVisible)
            {
                step.Name.Arrange(new Rect(x, numberTop + numberHeight + RowGap, column, step.Name.DesiredSize.Height));
                bottom = Math.Max(bottom, numberTop + numberHeight + RowGap + step.Name.DesiredSize.Height);
            }
        }

        // Half the gap either side, so two columns flashing at once meet but do not overlap.
        for (int index = 0; index < _steps.Count; index++)
        {
            double x = index * (column + Gap);
            _steps[index].Flash.Arrange(new Rect(x - Gap / 2, top - FlashMargin, column + Gap, bottom - top + 2 * FlashMargin));
        }

        return finalSize;
    }

    private double ColumnWidth(double width)
    {
        int count = Math.Max(1, _steps.Count);

        return Math.Max(4, (width - (count - 1) * Gap) / count);
    }

    // The parts are kept where the rail has as many steps as before, so a running module keeps moving.
    private void Rebuild()
    {
        IReadOnlyList<RailStep> steps = Steps ?? [];
        bool fresh = steps.Count != _steps.Count;

        if (fresh)
        {
            foreach (StepParts parts in _steps)
            {
                Children.Remove(parts.Flash);
                Children.Remove(parts.Module);
                Children.Remove(parts.Number);
                Children.Remove(parts.Name);
            }

            _steps.Clear();

            // The flashes go first, so they lie under every module and name.
            foreach (RailStep _ in steps)
            {
                StepParts parts = new(new Border { Classes = { "flash" } }, new RailModule(), Text("numeral"), Text("step"));
                _steps.Add(parts);
                Children.Insert(0, parts.Flash);
                Children.Add(parts.Module);
                Children.Add(parts.Number);
                Children.Add(parts.Name);
            }
        }

        for (int index = 0; index < steps.Count; index++)
        {
            RailStep step = steps[index];
            StepParts parts = _steps[index];

            if (!fresh && step.State != parts.Module.State && step.State is ConsoleStepState.Done or ConsoleStepState.Failed)
            {
                StartFlash(parts.Flash, step.State);
            }

            parts.Module.State = step.State;
            parts.Module.Percent = step.Percent;
            parts.Number.Text = step.Number;
            parts.Name.Text = step.Name;
            SetTone(parts.Number, step.State);
            SetTone(parts.Name, step.State);
            Avalonia.Automation.AutomationProperties.SetName(parts.Module, step.Description);
        }

        foreach (PhaseParts parts in _phases)
        {
            Children.Remove(parts.Label);
            Children.Remove(parts.Line);
        }

        _phases.Clear();

        foreach (RailPhase phase in Phases ?? [])
        {
            PhaseParts parts = new(Text("body", "ink2"), new Rectangle { Classes = { "phaseLine" } }, phase.Steps);
            parts.Label.Text = phase.Label;
            parts.Label.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            parts.Label.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            _phases.Add(parts);
            Children.Add(parts.Label);
            Children.Add(parts.Line);
        }

        InvalidateMeasure();
    }

    private static TextBlock Text(params string[] classes)
    {
        TextBlock text = new() { VerticalAlignment = VerticalAlignment.Top, MaxLines = 3 };
        text.Classes.AddRange(classes);

        return text;
    }

    // The running step's number is blue and its name bold; the steps still to come are quieter.
    private static void SetTone(TextBlock text, ConsoleStepState state)
    {
        text.Classes.Set("running", state == ConsoleStepState.Running);
        text.Classes.Set("waiting", state == ConsoleStepState.Pending);
        text.Classes.Set("failed", state == ConsoleStepState.Failed);
    }

    // The column in the state's colour, fading. Only a rail on the screen flashes, and Rebuild leaves out a run shown for
    // the first time.
    private void StartFlash(Border flash, ConsoleStepState state)
    {
        if (!Motion.IsEnabled || !this.IsAttachedToVisualTree())
        {
            return;
        }

        flash.Classes.Set("done", state == ConsoleStepState.Done);
        flash.Classes.Set("failed", state == ConsoleStepState.Failed);

        Animation fade = new()
        {
            Duration = Motion.Flash,
            Easing = Motion.Standard,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, FlashStrength) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };

        _ = fade.RunAsync(flash);
    }

    private sealed record StepParts(Border Flash, RailModule Module, TextBlock Number, TextBlock Name);

    private sealed record PhaseParts(TextBlock Label, Rectangle Line, int Steps);
}
