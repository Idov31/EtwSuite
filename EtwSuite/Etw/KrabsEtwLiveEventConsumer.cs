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
    private Provider? _provider;
    private EtwTraceSessionDescriptor? _traceSession;
    private string _providerName = string.Empty;
    private Guid _providerId;
    private bool _disposed;

    public ChannelReader<EtwLiveEventRecord> Events => _events.Reader;

    public async Task StartAsync(
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

        EtwTraceSessionDescriptor traceSession = EtwTraceSessionNameResolver.ResolveSession(options, "EtwSuite-", 48);
        var trace = new UserTrace(traceSession.SessionName);
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
        _provider = provider;
        _traceSession = traceSession;
        _traceTask = Task.Factory.StartNew(
            trace.Start,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Task completed = await Task.WhenAny(_traceTask, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        if (completed != _traceTask)
        {
            return;
        }

        try
        {
            await _traceTask;
        }
        catch (Exception ex)
        {
            await StopAsync();
            throw CreateStartException(ex);
        }

        await StopAsync();
        throw new InvalidOperationException("The ETW trace session ended before live consumption started.");
    }

    public async Task StopAsync()
    {
        UserTrace? trace = _trace;
        Task? traceTask = _traceTask;
        Provider? provider = _provider;
        EtwTraceSessionDescriptor? traceSession = _traceSession;
        _trace = null;
        _traceTask = null;
        _provider = null;
        _traceSession = null;

        if (trace is null)
        {
            return;
        }

        if (provider is not null)
        {
            provider.OnEvent -= HandleEvent;
            provider.OnError -= HandleError;
        }

        if (traceSession?.CanStopSession == false)
        {
            return;
        }

        try
        {
            trace.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ETW trace stop failed: {ex.Message}");
        }
        finally
        {
            if (traceTask is not null)
            {
                try
                {
                    await traceTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW trace task ended during stop: {ex.Message}");
                }
            }

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
            string type = MapPayloadInputType(record, property);
            payload.Add(new EtwPayloadValue(name, type, ReadPayloadValue(record, name, type)));
        }

        return payload;
    }

    private static string MapPayloadInputType(IEventRecord record, Property property)
    {
        string type = TdhInputTypeMapper.Map((uint)property.Type);
        return record.DecodingSource == DecodingSource.Wbem && type == "WideString"
            ? "ReverseCountedWideString"
            : type;
    }

    private static string ReadPayloadValue(IEventRecord record, string name, string type)
    {
        try
        {
            return type switch
            {
                "WideString" => record.GetUnicodeString(name, string.Empty),
                "AnsiString" => record.GetAnsiString(name, string.Empty),
                "CountedWideString" or "CountedString" or "ManifestCountedWideString" or "ManifestCountedString" => ReadWideCountedString(record, name, bigEndian: false),
                "CountedAnsiString" or "ManifestCountedAnsiString" => ReadCountedString(record, name, wide: false, bigEndian: false),
                "ReverseCountedWideString" or "ReversedCountedString" or "ReversedCountedWideString" => ReadReverseCountedWideString(record, name),
                "ReverseCountedAnsiString" or "ReversedCountedAnsiString" => ReadCountedString(record, name, wide: false, bigEndian: true),
                "NonNullTerminatedWideString" or "NonNullTerminatedString" or "UnicodeChar" => ReadNonNullTerminatedString(record, name, wide: true),
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

    private static string ReadWideCountedString(IEventRecord record, string name, bool bigEndian)
    {
        if (record.TryGetCountedString(name, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return ReadCountedString(record, name, wide: true, bigEndian);
    }

    private static string ReadReverseCountedWideString(IEventRecord record, string name)
    {
        return record.TryGetBinary(name, out byte[]? bytes)
            ? DecodeReverseCountedWideString(bytes)
            : string.Empty;
    }

    internal static string DecodeReverseCountedWideString(byte[] bytes)
    {
        if (bytes.Length < sizeof(ushort))
        {
            return string.Empty;
        }

        int countOffset = 0;

        if (bytes.Length >= sizeof(ushort) + 1)
        {
            int countAtStart = (bytes[0] << 8) | bytes[1];
            int countAfterPrefix = (bytes[1] << 8) | bytes[2];
            int availableAtStart = bytes.Length - sizeof(ushort);
            int availableAfterPrefix = bytes.Length - sizeof(ushort) - 1;

            if ((countAtStart == 0 || countAtStart > availableAtStart)
                && countAfterPrefix != 0
                && countAfterPrefix <= availableAfterPrefix + 1)
            {
                countOffset = 1;
            }
        }

        int dataBytes = (bytes[countOffset] << 8) | bytes[countOffset + 1];
        int availableDataBytes = bytes.Length - countOffset - sizeof(ushort);

        if (dataBytes > availableDataBytes)
        {
            dataBytes = availableDataBytes;
        }

        dataBytes &= ~1;
        return DecodeString(bytes.AsSpan(countOffset + sizeof(ushort), dataBytes), wide: true);
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

    private static Exception CreateStartException(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => new UnauthorizedAccessException(
                "Administrator privileges or tracing/logging group membership are required to consume this provider.",
                exception),
            FileNotFoundException => new FileNotFoundException(
                "The ETW live consumer native dependency could not be loaded. Reinstall or republish EtwSuite with the Microsoft.O365.Security.Native.ETW runtime files.",
                exception),
            _ => new InvalidOperationException("The ETW trace session failed to start.", exception)
        };
    }
}
