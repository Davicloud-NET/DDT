// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

public sealed class SettingsSectionApi<TValues>(
    SettingsSectionDefinition definition,
    Func<object, TValues> toValues,
    Func<TValues, object> toOptions) : SettingsSectionApi
    where TValues : class
{
    public override SettingsSectionDefinition Definition => definition;

    public TValues Values(object options) => toValues(options);

    // The whole section as its option class serializes it. The save ignores fields that configuration locks.
    public JsonObject Document(TValues values) => definition.Write(toOptions(values));

    public object Options(TValues values) => toOptions(values);

    public override object View(SettingsSectionState state, IReadOnlyList<SettingMessage> warnings, IReadOnlyList<SettingApplyState>? apply) =>
        TypedView(state, warnings, apply);

    public SettingsSectionView<TValues> TypedView(
        SettingsSectionState state,
        IReadOnlyList<SettingMessage> warnings,
        IReadOnlyList<SettingApplyState>? apply)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(warnings);

        return new SettingsSectionView<TValues>(
            state.Name,
            state.Version,
            state.UpdatedUtc,
            state.UpdatedBy,
            toValues(state.Options),
            state.Secrets,
            [.. state.Locks.Select(settingLock => new SettingLock(
                settingLock.Field.Name,
                settingLock.ConfigurationKey,
                SettingsSectionDefinition.EnvironmentVariable(settingLock.ConfigurationKey),
                settingLock.Source,
                settingLock.StoredDiffers))],
            [.. state.Problems.Select(problem => new SettingMessage(definition.PageName(problem.Field), problem.Message, null, problem.Text))],
            [
                .. state.Warnings.Select(warning => new SettingMessage(definition.PageName(warning.Field), warning.Message, warning.Code, warning.Text)),
                .. warnings,
            ],
            apply,
            [.. definition.Fields.Where(field => field.Reauthenticate).Select(field => field.Name)]);
    }
}
