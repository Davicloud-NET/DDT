// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// A boot port connection: the address it really comes from, and whether it carried the Linux boot environment's content.
public sealed class BootClient(IPAddress address, Func<IDisposable>? track = null) : IDisposable
{
    private readonly Lock _lock = new();
    private IDisposable? _tracked;
    private bool _ended;

    public IPAddress Address { get; } = address ?? throw new ArgumentNullException(nameof(address));

    // From here on the machine's next start resets the connection. One that only ever carried Windows PE never is.
    public void CarriesLinux()
    {
        lock (_lock)
        {
            if (!_ended)
            {
                _tracked ??= track?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _ended = true;
            _tracked?.Dispose();
            _tracked = null;
        }
    }
}
