// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DDT.Server.Settings;

// Holds the snapshot every consumer reads. Current starts from configuration and the code defaults, so logging has its
// levels before the store is loaded. Publish takes rows the store read, keeps the newest version of each section, and
// rebuilds the snapshot under one lock, so two saves of different sections cannot drop each other's change. It has no
// database access.
public sealed partial class DdtSettings
{
    private readonly Lock _lock = new();
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider? _services;
    private ImmutableDictionary<string, StoredSettingsSection> _stored = ImmutableDictionary.Create<string, StoredSettingsSection>(StringComparer.Ordinal);
    private SettingsSnapshot _current;
    private CancellationTokenSource _changed = new();
    private volatile bool _keyRingReadable = true;

    // The logger is taken from the services only when a change fails, because the logger factory itself reads the
    // settings, and taking a logger here would make each wait for the other.
    public DdtSettings(IConfiguration configuration, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
        _services = services;
        _current = SettingsSnapshot.Build(_stored, configuration);
    }

    public SettingsSnapshot Current => Volatile.Read(ref _current);

    // False when this process cannot read the key ring the stored secrets were encrypted with. It then saves nothing.
    public bool KeyRingReadable
    {
        get => _keyRingReadable;
        internal set => _keyRingReadable = value;
    }

    // Fires once, at the next publish. Framework options that read the snapshot drop their cached value on it.
    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changed).Token);

    public SettingsSnapshot Publish(IEnumerable<StoredSettingsSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        CancellationTokenSource changed;
        SettingsSnapshot snapshot;

        lock (_lock)
        {
            ImmutableDictionary<string, StoredSettingsSection>.Builder stored = _stored.ToBuilder();

            // A poll that read a row before a save of this process finished must not bring the older version back.
            foreach (StoredSettingsSection section in sections)
            {
                if (!stored.TryGetValue(section.Section, out StoredSettingsSection? held) || held.Version <= section.Version)
                {
                    stored[section.Section] = section;
                }
            }

            _stored = stored.ToImmutable();
            snapshot = SettingsSnapshot.Build(_stored, _configuration);
            Volatile.Write(ref _current, snapshot);
            changed = _changed;
            _changed = new CancellationTokenSource();
        }

        // Outside the lock: the callbacks read Current, and one may lead to another publish. The framework rebuilds options
        // in these callbacks, and one that fails must not undo a save that is written already; whoever uses those options
        // reports the failure.
        try
        {
            changed.Cancel();
        }
        catch (AggregateException exception)
        {
            if (_services?.GetService<ILogger<DdtSettings>>() is { } logger)
            {
                LogChangeFailed(logger, exception);
            }
        }

        return snapshot;
    }

    // What the snapshot would be with one section replaced, for a save to check before it writes. Saving names the
    // section for the rules checked only on a save.
    public SettingsSnapshot Preview(StoredSettingsSection section, string? saving)
    {
        ArgumentNullException.ThrowIfNull(section);

        return SettingsSnapshot.Build(Volatile.Read(ref _stored).SetItem(section.Section, section), _configuration, saving);
    }

    [LoggerMessage(EventId = 953, Level = LogLevel.Warning, Message = "A component that follows the settings could not take the change")]
    private static partial void LogChangeFailed(ILogger logger, Exception exception);
}
