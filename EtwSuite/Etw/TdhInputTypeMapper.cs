using System.Management;

namespace EtwSuite.Etw;

internal static class TdhInputTypeMapper
{
    public static string Map(uint inputType)
    {
        return inputType switch
        {
            0 => "Null",
            1 => "WideString",
            2 => "AnsiString",
            3 => "Int8",
            4 => "UInt8",
            5 => "Short",
            6 => "UShort",
            7 => "Integer",
            8 => "UInteger",
            9 => "Int64",
            10 => "UInt64",
            11 => "Float",
            12 => "Double",
            13 => "Boolean",
            14 => "Binary",
            15 => "Guid",
            16 => "Pointer",
            17 => "FileTime",
            18 => "SystemTime",
            19 => "Sid",
            20 => "HexInt32",
            21 => "HexInt64",
            22 => "ManifestCountedWideString",
            23 => "ManifestCountedAnsiString",
            24 => "Reserved",
            25 => "ManifestCountedBinary",
            300 => "CountedWideString",
            301 => "CountedAnsiString",
            302 => "ReverseCountedWideString",
            303 => "ReverseCountedAnsiString",
            304 => "NonNullTerminatedWideString",
            305 => "NonNullTerminatedAnsiString",
            306 => "UnicodeChar",
            307 => "AnsiChar",
            308 => "SizeT",
            309 => "HexDump",
            310 => "WbemSid",
            _ => $"Unknown ({inputType})"
        };
    }

    public static string MapWmi(CimType type, string? stringTermination, string? extension = null, string? format = null)
    {
        // MOF defaults to null termination; RString/RWString are null-terminated extensions.
        // https://learn.microsoft.com/en-us/windows/win32/etw/event-tracing-mof-qualifiers
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.Trim().ToUpperInvariant() switch
            {
                "RWSTRING" => "WideString",
                "RSTRING" => "AnsiString",
                "SID" => "WbemSid",
                _ => type == CimType.Object ? extension : MapWmi(type, stringTermination, format: format)
            };
        }

        if (type == CimType.String)
        {
            bool isWide = string.Equals(format?.Trim(), "w", StringComparison.OrdinalIgnoreCase);
            string termination = stringTermination?.Trim() ?? string.Empty;
            return termination.ToUpperInvariant() switch
            {
                "" or "NULLTERMINATED" => isWide ? "WideString" : "AnsiString",
                "COUNTED" => isWide ? "CountedWideString" : "CountedAnsiString",
                "REVERSECOUNTED" => isWide ? "ReverseCountedWideString" : "ReverseCountedAnsiString",
                "NOTCOUNTED" => isWide ? "NonNullTerminatedWideString" : "NonNullTerminatedAnsiString",
                _ => $"Unknown (StringTermination: {termination})"
            };
        }

        return type switch
        {
            CimType.Boolean => "Boolean",
            CimType.Char16 => "UnicodeChar",
            CimType.DateTime => "SystemTime",
            CimType.Object => "Struct",
            CimType.Real32 => "Float",
            CimType.Real64 => "Double",
            CimType.Reference => "Pointer",
            CimType.SInt8 => "Int8",
            CimType.SInt16 => "Short",
            CimType.SInt32 => "Integer",
            CimType.SInt64 => "Int64",
            CimType.UInt8 => "UInt8",
            CimType.UInt16 => "UShort",
            CimType.UInt32 => "UInteger",
            CimType.UInt64 => "UInt64",
            _ => type.ToString()
        };
    }
}
