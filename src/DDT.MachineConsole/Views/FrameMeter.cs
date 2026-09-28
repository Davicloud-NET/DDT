// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace DDT.MachineConsole.Views;

// Counts the frames the console draws while something on it moves, for whoever wants to know whether its motion stays
// smooth where it runs, such as in Windows PE. With DDT_CONSOLE_FRAMES naming a file, each change the console animates
// adds a line to it: what moved, the size of the screen in pixels, the frames drawn while it moved, the frames per
// second, the longest time between two of them and how long the first took to come, in ms. A frame counts once the
// renderer has drawn it.
internal sealed class FrameMeter
{
    public const string Variable = "DDT_CONSOLE_FRAMES";

    private static readonly TimeSpan s_settle = TimeSpan.FromMilliseconds(150);

    private readonly TopLevel _top;
    private readonly string _path;
    private readonly List<long> _drawn = [];
    private string? _what;
    private long _start;
    private TimeSpan _length;
    private int _measurement;

    private FrameMeter(TopLevel top, string path)
    {
        _top = top;
        _path = path;
    }

    public static FrameMeter? For(TopLevel top) =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path ? new FrameMeter(top, path) : null;

    // What a key press is called in the file: the keys that work the console by name, and any other key only as a key,
    // since it may be part of a password.
    public static string KeyName(Key key) =>
        key is >= Key.F1 and <= Key.F24 or Key.Escape or Key.Enter or Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right
            ? $"key {key}"
            : "key";

    // Counts the frames of what moves now, for as long as it moves. Something else that starts to move ends the count.
    public void Measure(string what, TimeSpan length)
    {
        if (_what is not null)
        {
            Write();
        }

        _what = what;
        _length = length;
        _start = Stopwatch.GetTimestamp();
        _measurement++;

        lock (_drawn)
        {
            _drawn.Clear();
        }

        _top.RequestAnimationFrame(Frame);
    }

    // Every frame of the animation sends the renderer a batch, and the time it has drawn it is the frame's.
    private void Frame(TimeSpan time)
    {
        if (_what is null)
        {
            return;
        }

        if (ElementComposition.GetElementVisual(_top)?.Compositor is { } compositor)
        {
            int measurement = _measurement;
            compositor.RequestCompositionBatchCommitAsync().Rendered.ContinueWith(
                _ => Drawn(measurement),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        if (Stopwatch.GetElapsedTime(_start) < _length)
        {
            _top.RequestAnimationFrame(Frame);
        }
        else
        {
            int measurement = _measurement;
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (measurement == _measurement && _what is not null)
                    {
                        Write();
                    }
                },
                s_settle);
        }
    }

    private void Drawn(int measurement)
    {
        long now = Stopwatch.GetTimestamp();

        lock (_drawn)
        {
            if (measurement == _measurement && (_drawn.Count == 0 || Stopwatch.GetElapsedTime(_drawn[^1], now).TotalMilliseconds > 1))
            {
                _drawn.Add(now);
            }
        }
    }

    private void Write()
    {
        string what = _what!;
        _what = null;
        long[] drawn;

        lock (_drawn)
        {
            drawn = [.. _drawn.Where(time => Stopwatch.GetElapsedTime(_start, time) <= _length)];
        }

        if (drawn.Length < 2)
        {
            return;
        }

        double seconds = Stopwatch.GetElapsedTime(drawn[0], drawn[^1]).TotalSeconds;
        double longest = 0;

        for (int index = 1; index < drawn.Length; index++)
        {
            longest = Math.Max(longest, Stopwatch.GetElapsedTime(drawn[index - 1], drawn[index]).TotalMilliseconds);
        }

        double scaling = _top.RenderScaling;
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{what}\t{_top.ClientSize.Width * scaling:0}x{_top.ClientSize.Height * scaling:0}\t{drawn.Length}\t{(drawn.Length - 1) / seconds:0.0}\t{longest:0.0}\t{Stopwatch.GetElapsedTime(_start, drawn[0]).TotalMilliseconds:0.0}\n");

        try
        {
            File.AppendAllText(_path, line);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A measurement that cannot be written is lost; the console goes on.
        }
    }
}
