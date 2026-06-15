using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using EtwSuite.Core;
using Microsoft.O365.Security.ETW;

namespace EtwSuite.Etw;

public sealed class KrabsEtwLiveEventConsumer : IEtwLiveEventConsumer
{
    private readonly Channel<EtwLiveEventRecord> _events = Channel.CreateBounded<EtwLiveEventRecord>(
        new BoundedChannelOptions(100_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly ConcurrentDictionary<uint, string> _processNames = new();
    private UserTrace? _trace;
    private Task? _traceTask;
    private string _providerName = string.Empty;
    private Guid _providerId;
    private bool _disposed;

    public ChannelReader<EtwLiveEventRecord> Events => _events.Reader;

    public Task StartAsync(
        EtwProviderEnableOptions options,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_trace is not null)
        {
            throw new InvalidOperationException("A trace session is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        _providerName = options.ProviderName;
        _providerId = options.ProviderId;

        string sessionName = CreateSessionName(options);
        var trace = new UserTrace(sessionName);
        var provider = new Provider(options.ProviderId)
        {
            Level = options.Level,
            Any = options.AnyKeyword,
            All = options.AllKeyword
        };

        provider.OnEvent += HandleEvent;
        provider.OnError += HandleError;
        trace.Enable(provider);

        _trace = trace;
        _traceTask = Task.Factory.StartNew(
            trace.Start,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        UserTrace? trace = _trace;
        Task? traceTask = _traceTask;
        _trace = null;
        _traceTask = null;

        if (trace is null)
        {
            return;
        }

        try
        {
            trace.Stop();
            if (traceTask is not null)
            {
                await traceTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            trace.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _events.Writer.TryComplete();
    }

    private void HandleEvent(IEventRecord record)
    {
        try
        {
            _events.Writer.TryWrite(CreateLiveEvent(record));
        }
        catch
        {
            // ETW callbacks must not throw back into krabsetw processing.
        }
    }

    private void HandleError(IEventRecordError error)
    {
        try
        {
            var payload = new[]
            {
                new EtwPayloadValue("Error", "String", error.Message)
            };

            _events.Writer.TryWrite(new EtwLiveEventRecord(
                DateTimeOffset.Now,
                _providerName,
                _providerId,
                "Decode error",
                0,
                0,
                0,
                0,
                0,
                "Unknown",
                0,
                payload));
        }
        catch
        {
        }
    }

    private EtwLiveEventRecord CreateLiveEvent(IEventRecord record)
    {
        var metadata = (IEventRecordMetadata)record;
        uint processId = metadata.ProcessId;

        return new EtwLiveEventRecord(
            DateTimeOffset.Now,
            string.IsNullOrWhiteSpace(record.ProviderName) ? _providerName : record.ProviderName,
            metadata.ProviderId,
            ResolveEventName(record, metadata),
            metadata.Id,
            metadata.Version,
            metadata.Opcode,
            metadata.Level,
            processId,
            ResolveProcessName(processId),
            metadata.ThreadId,
            ReadPayload(record));
    }

    private static string ResolveEventName(IEventRecord record, IEventRecordMetadata metadata)
    {
        if (IsMeaningfulEventLabel(record.Name))
        {
            return record.Name;
        }

        if (IsMeaningfulEventLabel(record.TaskName))
        {
            return record.TaskName;
        }

        return $"Event {metadata.Id}";
    }

    private static bool IsMeaningfulEventLabel(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("Uncategorized", StringComparison.OrdinalIgnoreCase);
    }

    private static List<EtwPayloadValue> ReadPayload(IEventRecord record)
    {
        var payload = new List<EtwPayloadValue>();
        foreach (Property property in record.Properties)
        {
            string name = property.Name;
            string type = MapInputType((uint)property.Type);
            payload.Add(new EtwPayloadValue(name, type, ReadPayloadValue(record, name, type)));
        }

        return payload;
    }

    private static string ReadPayloadValue(IEventRecord record, string name, string type)
    {
        try
        {
            return type switch
            {
                "WideString" => record.GetUnicodeString(name, string.Empty),
                "AnsiString" => record.GetAnsiString(name, string.Empty),
                "CountedString" or "ManifestCountedString" => ReadCountedString(record, name, wide: true, bigEndian: false),
                "CountedAnsiString" or "ManifestCountedAnsiString" => ReadCountedString(record, name, wide: false, bigEndian: false),
                "ReversedCountedString" => ReadCountedString(record, name, wide: true, bigEndian: true),
                "ReversedCountedAnsiString" => ReadCountedString(record, name, wide: false, bigEndian: true),
                "NonNullTerminatedString" or "UnicodeChar" => ReadNonNullTerminatedString(record, name, wide: true),
                "NonNullTerminatedAnsiString" or "AnsiChar" => ReadNonNullTerminatedString(record, name, wide: false),
                "ManifestCountedBinary" => ReadCountedBinary(record, name),
                "Int8" => record.GetInt8(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInt8" => record.GetUInt8(name, 0).ToString(CultureInfo.InvariantCulture),
                "Short" => record.GetInt16(name, 0).ToString(CultureInfo.InvariantCulture),
                "UShort" => record.GetUInt16(name, 0).ToString(CultureInfo.InvariantCulture),
                "Integer" => record.GetInt32(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInteger" => record.GetUInt32(name, 0).ToString(CultureInfo.InvariantCulture),
                "Int64" => record.GetInt64(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInt64" => record.GetUInt64(name, 0).ToString(CultureInfo.InvariantCulture),
                "HexInt32" => FormatHex(record.GetUInt32(name, 0), 8),
                "HexInt64" => FormatHex(record.GetUInt64(name, 0), 16),
                "Pointer" or "SizeT" => ReadPointer(record, name),
                "Float" => ReadFloat(record, name),
                "Double" => ReadDouble(record, name),
                "Boolean" => ReadBoolean(record, name),
                "Guid" => ReadGuid(record, name),
                "Sid" or "WbemSid" => ReadSid(record, name),
                "Binary" or "HexDump" => Convert.ToHexString(record.GetBinary(name)),
                "FileTime" or "SystemTime" => Convert
                    .ToDateTime(record.GetDateTime(name, DateTime.MinValue), CultureInfo.InvariantCulture)
                    .ToString("O", CultureInfo.InvariantCulture),
                _ => ReadBestEffortValue(record, name)
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatHex(ulong value, int digits)
    {
        return "0x" + value.ToString("X" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // Counted strings store a 16-bit byte count (little-endian, or big-endian for the reversed
    // variants) followed by the character bytes. The field is not null-terminated, so it cannot be
    // read with the null-terminated string getters. Field size is the byte count, not a character
    // count.
    private static string ReadCountedString(IEventRecord record, string name, bool wide, bool bigEndian)
    {
        if (!record.TryGetBinary(name, out byte[]? bytes) || bytes is not { Length: >= 2 })
        {
            return string.Empty;
        }

        int count = bigEndian
            ? (bytes[0] << 8) | bytes[1]
            : bytes[0] | (bytes[1] << 8);

        // When the raw property bytes include the count prefix, the count matches the remaining
        // byte length; otherwise the buffer is already just the character data.
        ReadOnlySpan<byte> data = count == bytes.Length - 2 ? bytes.AsSpan(2, count) : bytes;
        return DecodeString(data, wide);
    }

    private static string ReadNonNullTerminatedString(IEventRecord record, string name, bool wide)
    {
        return record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: > 0 }
            ? DecodeString(bytes, wide)
            : string.Empty;
    }

    private static string ReadCountedBinary(IEventRecord record, string name)
    {
        if (!record.TryGetBinary(name, out byte[]? bytes) || bytes is not { Length: >= 2 })
        {
            return string.Empty;
        }

        int count = bytes[0] | (bytes[1] << 8);
        ReadOnlySpan<byte> data = count == bytes.Length - 2 ? bytes.AsSpan(2, count) : bytes;
        return Convert.ToHexString(data);
    }

    private static string DecodeString(ReadOnlySpan<byte> data, bool wide)
    {
        string value = wide ? Encoding.Unicode.GetString(data) : Encoding.UTF8.GetString(data);
        return value.TrimEnd('\0');
    }

    private static string ReadGuid(IEventRecord record, string name)
    {
        return record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: 16 }
            ? new Guid(bytes).ToString("D", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ReadFloat(IEventRecord record, string name)
    {
        return record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: >= 4 }
            ? BitConverter.ToSingle(bytes, 0).ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ReadDouble(IEventRecord record, string name)
    {
        return record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: >= 8 }
            ? BitConverter.ToDouble(bytes, 0).ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ReadBoolean(IEventRecord record, string name)
    {
        return record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: > 0 }
            ? (Array.Exists(bytes, value => value != 0) ? "True" : "False")
            : string.Empty;
    }

    private static string ReadPointer(IEventRecord record, string name)
    {
        if (record.TryGetBinary(name, out byte[]? bytes))
        {
            if (bytes is { Length: >= 8 })
            {
                return FormatHex(BitConverter.ToUInt64(bytes, 0), 16);
            }

            if (bytes is { Length: >= 4 })
            {
                return FormatHex(BitConverter.ToUInt32(bytes, 0), 8);
            }
        }

        return string.Empty;
    }

    private static string ReadSid(IEventRecord record, string name)
    {
        if (record.TryGetBinary(name, out byte[]? bytes) && bytes is { Length: >= 8 })
        {
            try
            {
                return new SecurityIdentifier(bytes, 0).Value;
            }
            catch (ArgumentException)
            {
                return Convert.ToHexString(bytes);
            }
        }

        return string.Empty;
    }

    private static string ReadBestEffortValue(IEventRecord record, string name)
    {
        if (record.TryGetUnicodeString(name, out string? wideValue))
        {
            return wideValue;
        }

        if (record.TryGetAnsiString(name, out string? ansiValue))
        {
            return ansiValue;
        }

        if (record.TryGetUInt64(name, out ulong unsigned64))
        {
            return unsigned64.ToString(CultureInfo.InvariantCulture);
        }

        if (record.TryGetInt64(name, out long signed64))
        {
            return signed64.ToString(CultureInfo.InvariantCulture);
        }

        if (record.TryGetUInt32(name, out uint unsigned32))
        {
            return unsigned32.ToString(CultureInfo.InvariantCulture);
        }

        if (record.TryGetInt32(name, out int signed32))
        {
            return signed32.ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return "Unknown";
        }

        return _processNames.GetOrAdd(processId, static id =>
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)id));
                return string.IsNullOrWhiteSpace(process.ProcessName) ? "Unknown" : process.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        });
    }

    private static string CreateSessionName(EtwProviderEnableOptions options)
    {
        string providerName = new([.. options.ProviderName
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(48)]);

        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = "Provider";
        }

        return $"EtwSuite-{providerName}-{Guid.NewGuid():N}"[..64];
    }

    private static string MapInputType(uint inputType)
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
            22 => "ManifestCountedString",
            23 => "ManifestCountedAnsiString",
            24 => "Reserved",
            25 => "ManifestCountedBinary",
            // The counted/reversed/non-terminated string input types are not contiguous with the
            // manifest types; TDH_IN_TYPE jumps to 300 for the WMI/MOF input types.
            300 => "CountedString",
            301 => "CountedAnsiString",
            302 => "ReversedCountedString",
            303 => "ReversedCountedAnsiString",
            304 => "NonNullTerminatedString",
            305 => "NonNullTerminatedAnsiString",
            306 => "UnicodeChar",
            307 => "AnsiChar",
            308 => "SizeT",
            309 => "HexDump",
            310 => "WbemSid",
            _ => $"Unknown ({inputType})"
        };
    }
}
