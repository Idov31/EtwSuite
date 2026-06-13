using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
                "Int8" => record.GetInt8(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInt8" => record.GetUInt8(name, 0).ToString(CultureInfo.InvariantCulture),
                "Short" => record.GetInt16(name, 0).ToString(CultureInfo.InvariantCulture),
                "UShort" => record.GetUInt16(name, 0).ToString(CultureInfo.InvariantCulture),
                "Integer" => record.GetInt32(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInteger" => record.GetUInt32(name, 0).ToString(CultureInfo.InvariantCulture),
                "Int64" => record.GetInt64(name, 0).ToString(CultureInfo.InvariantCulture),
                "UInt64" => record.GetUInt64(name, 0).ToString(CultureInfo.InvariantCulture),
                "Pointer" => ReadBestEffortValue(record, name),
                "Binary" => Convert.ToHexString(record.GetBinary(name)),
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
            22 => "WideCountedString",
            23 => "AnsiCountedString",
            24 => "Reserved",
            25 => "CountedBinary",
            26 => "CountedString",
            27 => "CountedAnsiString",
            28 => "ReversedCountedWideString",
            29 => "ReversedCountedAnsiString",
            30 => "NonNullTerminatedWideString",
            31 => "NonNullTerminatedAnsiString",
            32 => "UnicodeChar",
            33 => "AnsiChar",
            34 => "SizeT",
            35 => "HexDump",
            36 => "WbemSid",
            _ => $"Unknown ({inputType})"
        };
    }
}
