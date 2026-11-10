// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;
using DDT.MachineConsole.Keyboard;
using DDT.MachineConsole.Screen;

namespace DDT.MachineConsole.Input;

// Reads every keyboard on a thread of its own and hands the keys to Avalonia in order, each with its text unless the
// key was handled, as Avalonia's X11 backend does. The raw input API is private to Avalonia: check it at each upgrade.
internal sealed class EvdevInputBackend(KeyboardTranslator translator, EvdevDevices devices, ScreenKeeper keeper) : IInputBackend
{
    private const int TickMilliseconds = 250;
    private const long ScanMilliseconds = 1000;

    private readonly HashSet<int> _dropped = [];
    private IInputRoot? _root;
    private IKeyboardDevice? _keyboard;
    private KeyboardLayout? _nextLayout;
    private (bool CapsLock, bool NumLock)? _lit;

    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _keyboard = AvaloniaLocator.Current.GetService<IKeyboardDevice>();
        new Thread(Run) { IsBackground = true, Name = "DDT keyboards" }.Start();
    }

    public void SetInputRoot(IInputRoot root) => _root = root;

    // From any thread; the input thread takes it before the next key.
    public void Use(KeyboardLayout layout) => Interlocked.Exchange(ref _nextLayout, layout);

    private void Run()
    {
        devices.Hold();
        IReadOnlyList<(int Id, EvdevDevice Device)> first = devices.Scan();
        translator.NumLock = first.Any(found => found.Device.NumLockLit());
        long scanned = Environment.TickCount64;

        while (true)
        {
            foreach ((int id, IReadOnlyList<EvdevEvent>? events) in devices.Wait(TickMilliseconds))
            {
                Handle(id, events);
            }

            if (keeper.Look())
            {
                Resync();
            }

            if (Environment.TickCount64 - scanned >= ScanMilliseconds)
            {
                // A new keyboard gets the lights too.
                if (devices.Scan().Count > 0)
                {
                    _lit = null;
                }

                scanned = Environment.TickCount64;
            }

            if (Interlocked.Exchange(ref _nextLayout, null) is { } layout)
            {
                translator.Layout = layout;
            }

            devices.GrabLate();
            Light();
        }
    }

    // A device that dropped events is read again from SYN_DROPPED to the next report, then asked what it holds.
    private void Handle(int id, IReadOnlyList<EvdevEvent>? events)
    {
        if (events is null)
        {
            _dropped.Remove(id);
            Send(translator.Forget(id));
            devices.Forget(id);

            return;
        }

        foreach (EvdevEvent input in events)
        {
            if (input is { Type: EvdevEvents.Sync, Code: EvdevEvents.SyncDropped })
            {
                _dropped.Add(id);
            }
            else if (_dropped.Contains(id))
            {
                if (input is { Type: EvdevEvents.Sync, Code: EvdevEvents.SyncReport })
                {
                    _dropped.Remove(id);
                    Send(translator.Resync(id, devices.Held(id)));
                }
            }
            else if (keeper.Present)
            {
                Send(translator.Feed(id, input));
            }
        }
    }

    // Back in front: keys pressed or let go meanwhile were the kernel's.
    private void Resync()
    {
        foreach (int id in devices.Ids)
        {
            Send(translator.Resync(id, devices.Held(id)));
        }
    }

    private void Light()
    {
        (bool, bool) now = (translator.CapsLock, translator.NumLock);

        if (_lit != now)
        {
            devices.Light(now.Item1, now.Item2);
            _lit = now;
        }
    }

    private void Send(IReadOnlyList<KeyStroke> strokes)
    {
        if (strokes.Count > 0)
        {
            Dispatcher.UIThread.Post(() => Deliver(strokes), DispatcherPriority.Send);
        }
    }

    private void Deliver(IReadOnlyList<KeyStroke> strokes)
    {
        if (_root is not { } root || _keyboard is not { } keyboard)
        {
            return;
        }

        foreach (KeyStroke stroke in strokes)
        {
            ulong time = (ulong)Environment.TickCount64;
            bool handled = false;

            if (stroke.Key != Key.None)
            {
                RawKeyEventType type = stroke.Down ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp;
                RawInputModifiers modifiers = (RawInputModifiers)stroke.Modifiers;
                RawKeyEventArgs key = new(keyboard, time, root, type, stroke.Key, modifiers, stroke.PhysicalKey, stroke.Text);
                keyboard.ProcessRawEvent(key);
                handled = key.Handled;
            }

            if (stroke is { Down: true, Text.Length: > 0 } && !handled)
            {
                keyboard.ProcessRawEvent(new RawTextInputEventArgs(keyboard, time, root, stroke.Text));
            }
        }
    }
}
