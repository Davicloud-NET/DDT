// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Input;

// Every keyboard under /dev/input, found again by looking, without udev. Only the input thread reads and forgets them;
// the screen holds or lets them go from any thread.
internal sealed class EvdevDevices : IDisposable
{
    private const string Folder = "/dev/input";

    private readonly Lock _gate = new();
    private readonly Dictionary<int, EvdevDevice> _devices = [];
    private int _next = 1;
    private bool _holding;

    // The new keyboards, by the number the translator knows them by.
    public IReadOnlyList<(int Id, EvdevDevice Device)> Scan()
    {
        List<(int, EvdevDevice)> found = [];
        string[] paths;

        try
        {
            paths = Directory.GetFiles(Folder, "event*");
        }
        catch (IOException)
        {
            return found;
        }

        lock (_gate)
        {
            HashSet<string> known = [.. _devices.Values.Select(device => device.Path)];

            // A path that was no keyboard is looked at again: the kernel gives its number to the next device.
            foreach (string path in paths.Where(path => !known.Contains(path)))
            {
                if (EvdevDevice.Open(path) is not { } device)
                {
                    continue;
                }

                if (_holding)
                {
                    _ = device.Grab();
                }

                _devices[_next] = device;
                found.Add((_next++, device));
            }
        }

        return found;
    }

    // While the console is in front: grabs every keyboard that holds no key now.
    public void Hold()
    {
        lock (_gate)
        {
            _holding = true;

            foreach (EvdevDevice device in _devices.Values)
            {
                _ = device.Grab();
            }
        }
    }

    // A keyboard that held a key at the last try.
    public void GrabLate()
    {
        lock (_gate)
        {
            foreach (EvdevDevice device in _devices.Values.Where(device => _holding && !device.Grabbed))
            {
                _ = device.Grab();
            }
        }
    }

    public void LetGo()
    {
        lock (_gate)
        {
            _holding = false;

            foreach (EvdevDevice device in _devices.Values)
            {
                device.Release();
            }
        }
    }

    // Waits for keys, then reads every keyboard that has some. Null events mean the keyboard is gone.
    public unsafe IReadOnlyList<(int Id, IReadOnlyList<EvdevEvent>? Events)> Wait(int milliseconds)
    {
        (int Id, int Descriptor)[] watched;

        lock (_gate)
        {
            watched = [.. _devices.Select(pair => (pair.Key, pair.Value.Descriptor))];
        }

        PollDescriptor* descriptors = stackalloc PollDescriptor[Math.Max(watched.Length, 1)];

        for (int i = 0; i < watched.Length; i++)
        {
            descriptors[i] = new PollDescriptor { Descriptor = watched[i].Descriptor, Events = Libc.PollIn };
        }

        if (watched.Length == 0)
        {
            Thread.Sleep(milliseconds);

            return [];
        }

        if (Libc.Poll(descriptors, (nuint)watched.Length, milliseconds) <= 0)
        {
            return [];
        }

        List<(int, IReadOnlyList<EvdevEvent>?)> read = [];

        lock (_gate)
        {
            for (int i = 0; i < watched.Length; i++)
            {
                short returned = descriptors[i].Returned;

                if (returned != 0 && _devices.TryGetValue(watched[i].Id, out EvdevDevice? device))
                {
                    bool broken = (returned & (Libc.PollError | Libc.PollHangUp | Libc.PollInvalid)) != 0;
                    read.Add((watched[i].Id, broken ? null : device.Read()));
                }
            }
        }

        return read;
    }

    public IReadOnlyList<int> Ids
    {
        get
        {
            lock (_gate)
            {
                return [.. _devices.Keys];
            }
        }
    }

    public IReadOnlyCollection<ushort> Held(int id)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(id, out EvdevDevice? device) ? device.Held() : [];
        }
    }

    public void Light(bool capsLock, bool numLock)
    {
        lock (_gate)
        {
            foreach (EvdevDevice device in _devices.Values)
            {
                device.Light(capsLock, numLock);
            }
        }
    }

    public void Forget(int id)
    {
        lock (_gate)
        {
            if (_devices.Remove(id, out EvdevDevice? device))
            {
                device.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (EvdevDevice device in _devices.Values)
            {
                device.Dispose();
            }

            _devices.Clear();
        }
    }
}
