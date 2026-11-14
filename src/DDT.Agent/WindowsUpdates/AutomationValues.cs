// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// VARIANTs in and out of a late-bound call, with the types Windows Update answers with. A text argument owns a BSTR
// that the call frees; an object argument is borrowed.
public static class AutomationValues
{
    // DISP_E_TYPEMISMATCH.
    public const int TypeMismatch = unchecked((int)0x80020005);

    public static ComVariant Text(string value) => ComVariant.Create(value);

    public static ComVariant Int(int value) => ComVariant.Create(value);

    public static ComVariant Unknown(nint pointer) => ComVariant.CreateRaw(VarEnum.VT_UNKNOWN, pointer);

    public static ComVariant Dispatch(nint pointer) => ComVariant.CreateRaw(VarEnum.VT_DISPATCH, pointer);

    // A null BSTR reads as null, like an empty answer.
    public static string? ToText(ComVariant value, string member) => value.VarType switch
    {
        VarEnum.VT_BSTR => value.GetRawDataRef<nint>() is var text && text != 0 ? Marshal.PtrToStringBSTR(text) : null,
        VarEnum.VT_EMPTY or VarEnum.VT_NULL => null,
        _ => throw Mismatch(member, value),
    };

    public static int ToInt(ComVariant value, string member) => value.VarType switch
    {
        VarEnum.VT_I4 or VarEnum.VT_INT or VarEnum.VT_UI4 or VarEnum.VT_ERROR => value.GetRawDataRef<int>(),
        VarEnum.VT_I2 => value.GetRawDataRef<short>(),
        _ => throw Mismatch(member, value),
    };

    // VARIANT_BOOL: -1 or 0.
    public static bool ToBool(ComVariant value, string member) =>
        value.VarType == VarEnum.VT_BOOL ? value.GetRawDataRef<short>() != 0 : throw Mismatch(member, value);

    // Sizes come as VT_DECIMAL.
    public static decimal ToDecimal(ComVariant value, string member) => value.VarType switch
    {
        VarEnum.VT_DECIMAL => value.As<decimal>(),
        VarEnum.VT_I4 => value.GetRawDataRef<int>(),
        VarEnum.VT_I8 => value.GetRawDataRef<long>(),
        VarEnum.VT_R8 => (decimal)value.GetRawDataRef<double>(),
        _ => throw Mismatch(member, value),
    };

    // An object's pointer, still owned by the VARIANT. Zero for none.
    public static nint ToPointer(ComVariant value, string member) => value.VarType switch
    {
        VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN => value.GetRawDataRef<nint>(),
        VarEnum.VT_EMPTY or VarEnum.VT_NULL => 0,
        _ => throw Mismatch(member, value),
    };

    private static ComCallException Mismatch(string member, ComVariant value) =>
        new(member, TypeMismatch, $"answered with VARIANT type {(int)value.VarType}");
}
