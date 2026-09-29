// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using Avalonia.Controls;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Views;

// When the agent moves to the next step or stage, the step's text animates in again, like a new screen would. A new
// percent for the same step only changes the number, and the rail moves by itself.
public sealed partial class RunView : UserControl
{
    private RunViewModel? _model;
    private (ConsoleStage Stage, Guid? Step)? _shown;

    public RunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as RunViewModel;
        _shown = Where(_model);

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
    }

    private static (ConsoleStage Stage, Guid? Step)? Where(RunViewModel? model) =>
        model is null ? null : (model.Stage, model.Run?.CurrentStepId);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        (ConsoleStage Stage, Guid? Step)? now = Where(_model);

        if (now == _shown)
        {
            return;
        }

        _shown = now;
        Motion.Renew(Hero);

        if (_model?.HasProblem == true)
        {
            Motion.Renew(Problems);
        }
    }
}
