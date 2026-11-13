// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// One Windows Update object, reached late-bound: members by name through GetIDsOfNames and Invoke. Holds one
// reference. Only the COM thread that made it may use it.
internal sealed unsafe class AutomationObject : IDisposable
{
    private const ushort Method = 1;
    private const ushort PropertyGet = 2;
    private const ushort PropertyPut = 4;
    private const int DispatchIdPropertyPut = -3;
    private const int DispatchException = unchecked((int)0x80020009);

    private static readonly StrategyBasedComWrappers s_wrappers = new();

    private readonly IDispatchNative _native;
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private nint _pointer;

    private AutomationObject(nint pointer)
    {
        _pointer = pointer;
        Marshal.AddRef(pointer);

        // Without UniqueInstance the wrapper is cached by identity, and one Dispose would end another wrapper's object.
        _native = (IDispatchNative)s_wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
    }

    // Stays valid while this object is not disposed.
    public nint Pointer => _pointer;

    public static AutomationObject Create(Guid classId)
    {
        int result = ComNativeMethods.CoCreateInstance(classId, 0, ComNativeMethods.AnyContext, ComNativeMethods.DispatchId, out nint created);

        if (result < 0)
        {
            throw new ComCallException($"CoCreateInstance {classId}", result, null);
        }

        try
        {
            return new AutomationObject(created);
        }
        finally
        {
            Marshal.Release(created);
        }
    }

    public string? GetText(string name, params ReadOnlySpan<ComVariant> args) =>
        Read(name, PropertyGet | Method, args, static (value, member) => AutomationValues.ToText(value, member));

    public int GetInt(string name, params ReadOnlySpan<ComVariant> args) =>
        Read(name, PropertyGet | Method, args, static (value, member) => AutomationValues.ToInt(value, member));

    public bool GetBool(string name) => Read(name, PropertyGet, [], static (value, member) => AutomationValues.ToBool(value, member));

    public decimal GetDecimal(string name) => Read(name, PropertyGet, [], static (value, member) => AutomationValues.ToDecimal(value, member));

    public AutomationObject? GetObject(string name, params ReadOnlySpan<ComVariant> args) => Read(name, PropertyGet | Method, args, ObjectOf);

    public AutomationObject? Call(string name, params ReadOnlySpan<ComVariant> args) => Read(name, Method, args, ObjectOf);

    public int CallInt(string name, params ReadOnlySpan<ComVariant> args) =>
        Read(name, Method, args, static (value, member) => AutomationValues.ToInt(value, member));

    // A method that must answer with an object.
    public AutomationObject CallForObject(string name, params ReadOnlySpan<ComVariant> args) =>
        Call(name, args) ?? throw new ComCallException(name, AutomationValues.TypeMismatch, "answered with no object");

    public void Put(string name, ComVariant value)
    {
        ComVariant ignored = Invoke(name, PropertyPut, [value]);
        ignored.Dispose();
    }

    public void Dispose()
    {
        if (_pointer == 0)
        {
            return;
        }

        ((ComObject)(object)_native).FinalRelease();
        Marshal.Release(_pointer);
        _pointer = 0;
    }

    private static AutomationObject? ObjectOf(ComVariant value, string member)
    {
        nint pointer = AutomationValues.ToPointer(value, member);

        if (pointer == 0)
        {
            return null;
        }

        if (value.VarType == VarEnum.VT_DISPATCH)
        {
            return new AutomationObject(pointer);
        }

        Guid dispatch = ComNativeMethods.DispatchId;
        int result = Marshal.QueryInterface(pointer, in dispatch, out nint asDispatch);

        if (result < 0)
        {
            throw new ComCallException(member + " as IDispatch", result, null);
        }

        try
        {
            return new AutomationObject(asDispatch);
        }
        finally
        {
            Marshal.Release(asDispatch);
        }
    }

    private T Read<T>(string name, ushort flags, ReadOnlySpan<ComVariant> args, Func<ComVariant, string, T> convert)
    {
        ComVariant result = Invoke(name, flags, args);

        try
        {
            return convert(result, name);
        }
        finally
        {
            result.Dispose();
        }
    }

    private int Id(string name)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            return id;
        }

        Guid none = Guid.Empty;
        int found;

        fixed (char* text = name)
        {
            char* names = text;
            int result = _native.GetIDsOfNames(&none, &names, 1, 0, &found);

            if (result < 0)
            {
                throw new ComCallException("GetIDsOfNames " + name, result, null);
            }
        }

        _ids[name] = found;

        return found;
    }

    private ComVariant Invoke(string name, ushort flags, ReadOnlySpan<ComVariant> args)
    {
        ObjectDisposedException.ThrowIf(_pointer == 0, this);

        try
        {
            return Invoke(Id(name), name, flags, args);
        }
        finally
        {
            FreeTexts(args);
        }
    }

    private ComVariant Invoke(int id, string name, ushort flags, ReadOnlySpan<ComVariant> args)
    {
        int count = args.Length;
        ComVariant* reversed = stackalloc ComVariant[Math.Max(count, 1)];

        for (int index = 0; index < count; index++)
        {
            reversed[index] = args[count - 1 - index];
        }

        bool put = flags == PropertyPut;
        int named = DispatchIdPropertyPut;
        DispatchParameters parameters = new()
        {
            Arguments = count == 0 ? null : reversed,
            ArgumentCount = (uint)count,
            NamedArguments = put ? &named : null,
            NamedArgumentCount = put ? 1u : 0u,
        };
        ComVariant result = default;
        ExceptionInfo exception = default;
        uint argumentError = 0;
        Guid none = Guid.Empty;
        int answer = _native.Invoke(id, &none, 0, flags, &parameters, put ? null : &result, &exception, &argumentError);

        if (answer < 0)
        {
            throw Failure(name, answer, ref exception);
        }

        return result;
    }

    // DISP_E_EXCEPTION carries Windows Update's own code in EXCEPINFO.
    private static ComCallException Failure(string name, int answer, ref ExceptionInfo exception)
    {
        string? description = exception.Description == 0 ? null : Marshal.PtrToStringBSTR(exception.Description);
        Marshal.FreeBSTR(exception.Source);
        Marshal.FreeBSTR(exception.Description);
        Marshal.FreeBSTR(exception.HelpFile);
        int code = answer == DispatchException && exception.SCode != 0 ? exception.SCode : answer;

        return new ComCallException(name, code, description);
    }

    private static void FreeTexts(ReadOnlySpan<ComVariant> args)
    {
        foreach (ComVariant arg in args)
        {
            if (arg.VarType == VarEnum.VT_BSTR)
            {
                arg.Dispose();
            }
        }
    }
}
