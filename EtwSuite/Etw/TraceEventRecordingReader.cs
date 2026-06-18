using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EtwSuite.Core;
using Microsoft.Diagnostics.Tracing;

namespace EtwSuite.Etw;

public sealed class TraceEventRecordingReader : IEtwRecordingReader
{
    private const string UnsupportedMessage = "This file type is not supported. Supported: .etl, .json, .csv.";

    public EtwRecordingFormat DetectFormat(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".etl" => EtwRecordingFormat.Etl,
            ".json" => EtwRecordingFormat.Json,
            ".csv" => EtwRecordingFormat.Csv,
            _ => EtwRecordingFormat.Unsupported,
        };
    }

    public async IAsyncEnumerable<IReadOnlyList<EtwLiveEventRecord>> ReadEventsAsync(
        string filePath,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new EtwRecordingException("Select a recording file to open.");
        }

        if (!File.Exists(filePath))
        {
            throw new EtwRecordingException("The selected recording file does not exist.");
        }

        batchSize = Math.Clamp(batchSize, 1, 10_000);
        EtwRecordingFormat format = DetectFormat(filePath);
        switch (format)
        {
            case EtwRecordingFormat.Etl:
                foreach (IReadOnlyList<EtwLiveEventRecord> batch in await Task.Run(
                    () => ReadEtl(filePath, batchSize, cancellationToken),
                    cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return batch;
                }

                break;
            case EtwRecordingFormat.Json:
                foreach (IReadOnlyList<EtwLiveEventRecord> batch in await Task.Run(
                    () => ReadJson(filePath, batchSize, GetOptions(), cancellationToken),
                    cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return batch;
                }

                break;
            case EtwRecordingFormat.Csv:
                foreach (IReadOnlyList<EtwLiveEventRecord> batch in await Task.Run(
                    () => ReadCsv(filePath, batchSize, cancellationToken),
                    cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return batch;
                }

                break;
            default:
                throw new EtwRecordingException(UnsupportedMessage);
        }
    }

    private static List<IReadOnlyList<EtwLiveEventRecord>> ReadEtl(
        string filePath,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var batches = new List<IReadOnlyList<EtwLiveEventRecord>>();
        var batch = new List<EtwLiveEventRecord>(batchSize);

        try
        {
            using var source = new ETWTraceEventSource(filePath);
            source.Dynamic.All += traceEvent =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldIncludeEtlEventProvider(traceEvent.ProviderName))
                {
                    return;
                }

                batch.Add(CreateEvent(traceEvent));
                if (batch.Count >= batchSize)
                {
                    batches.Add([.. batch]);
                    batch.Clear();
                }
            };

            source.Process();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EtwRecordingException("The ETL recording could not be parsed.", ex);
        }

        if (batch.Count > 0)
        {
            batches.Add([.. batch]);
        }

        return batches;
    }

    internal static bool ShouldIncludeEtlEventProvider(string? providerName)
    {
        return !string.Equals(providerName, "MSNT_SystemTrace", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions GetOptions()
    {
        return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private static List<IReadOnlyList<EtwLiveEventRecord>> ReadJson(
        string filePath,
        int batchSize,
JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            JsonExportEvent[]? events = JsonSerializer.Deserialize<JsonExportEvent[]>(
                stream,
                options: options);

            return events is null
                ? throw new EtwRecordingException("The JSON recording is empty or incompatible.")
                : ToBatches(events.Select(ToRecord), batchSize, cancellationToken);
        }
        catch (EtwRecordingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EtwRecordingException("The JSON recording could not be parsed.", ex);
        }
    }

    private static List<IReadOnlyList<EtwLiveEventRecord>> ReadCsv(
        string filePath,
        int batchSize,
        CancellationToken cancellationToken)
    {
        try
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length == 0)
            {
                throw new EtwRecordingException("The CSV recording is empty.");
            }

            string[] header = [.. ParseCsvLine(lines[0])];
            string[] expectedHeader =
            [
                "Time",
                "Provider",
                "Event",
                "Id",
                "Version",
                "Opcode",
                "Level",
                "ProcessId",
                "ProcessName",
                "ThreadId",
                "Parameters",
            ];

            if (!header.SequenceEqual(expectedHeader, StringComparer.Ordinal))
            {
                throw new EtwRecordingException("The CSV recording is not an EtwSuite event export.");
            }

            var records = new List<EtwLiveEventRecord>();
            foreach (string line in lines.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] fields = [.. ParseCsvLine(line)];
                if (fields.Length != expectedHeader.Length)
                {
                    throw new EtwRecordingException("The CSV recording has an invalid event row.");
                }

                records.Add(ToRecord(fields));
            }

            return ToBatches(records, batchSize, cancellationToken);
        }
        catch (EtwRecordingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EtwRecordingException("The CSV recording could not be parsed.", ex);
        }
    }

    private static EtwLiveEventRecord CreateEvent(TraceEvent traceEvent)
    {
        return new EtwLiveEventRecord(
            new DateTimeOffset(traceEvent.TimeStamp),
            string.IsNullOrWhiteSpace(traceEvent.ProviderName) ? "Unknown" : traceEvent.ProviderName,
            traceEvent.ProviderGuid,
            string.IsNullOrWhiteSpace(traceEvent.EventName) ? $"Event {(ushort)traceEvent.ID}" : traceEvent.EventName,
            (ushort)traceEvent.ID,
            (byte)traceEvent.Version,
            (byte)traceEvent.Opcode,
            (byte)traceEvent.Level,
            (uint)Math.Max(0, traceEvent.ProcessID),
            string.IsNullOrWhiteSpace(traceEvent.ProcessName) ? "Unknown" : traceEvent.ProcessName,
            (uint)Math.Max(0, traceEvent.ThreadID),
            ReadPayload(traceEvent));
    }

    private static List<EtwPayloadValue> ReadPayload(TraceEvent traceEvent)
    {
        var payload = new List<EtwPayloadValue>();
        foreach (string name in traceEvent.PayloadNames)
        {
            object? value;
            try
            {
                value = traceEvent.PayloadByName(name);
            }
            catch
            {
                value = null;
            }

            payload.Add(new EtwPayloadValue(
                name,
                value?.GetType().Name ?? "Unknown",
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return payload;
    }

    private static EtwLiveEventRecord ToRecord(JsonExportEvent exportEvent)
    {
        return new EtwLiveEventRecord(
            ParseExportTime(exportEvent.Time),
            exportEvent.Provider ?? string.Empty,
            Guid.Empty,
            exportEvent.Event ?? string.Empty,
            exportEvent.Id,
            exportEvent.Version,
            exportEvent.Opcode,
            exportEvent.Level,
            exportEvent.ProcessId,
            exportEvent.ProcessName ?? string.Empty,
            exportEvent.ThreadId,
            exportEvent.Parameters?.Select(parameter => new EtwPayloadValue(
                parameter.Name ?? string.Empty,
                parameter.Type ?? string.Empty,
                parameter.Value ?? string.Empty)).ToArray() ?? []);
    }

    private static EtwLiveEventRecord ToRecord(string[] fields)
    {
        return new EtwLiveEventRecord(
            ParseExportTime(fields[0]),
            fields[1],
            Guid.Empty,
            fields[2],
            ParseUInt16(fields[3]),
            ParseByte(fields[4]),
            ParseByte(fields[5]),
            ParseByte(fields[6]),
            ParseUInt32(fields[7]),
            fields[8],
            ParseUInt32(fields[9]),
            [new EtwPayloadValue("Parameters", "String", fields[10])]);
    }

    private static DateTimeOffset ParseExportTime(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset timestamp))
        {
            return timestamp;
        }

        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out TimeSpan time))
        {
            return new DateTimeOffset(DateTime.Today.Add(time));
        }

        return DateTimeOffset.MinValue;
    }

    private static byte ParseByte(string value)
    {
        return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result) ? result : (byte)0;
    }

    private static ushort ParseUInt16(string value)
    {
        return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort result) ? result : (ushort)0;
    }

    private static uint ParseUInt32(string value)
    {
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result) ? result : 0;
    }

    private static List<IReadOnlyList<EtwLiveEventRecord>> ToBatches(
        IEnumerable<EtwLiveEventRecord> records,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var batches = new List<IReadOnlyList<EtwLiveEventRecord>>();
        var batch = new List<EtwLiveEventRecord>(batchSize);
        foreach (EtwLiveEventRecord record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(record);
            if (batch.Count >= batchSize)
            {
                batches.Add([.. batch]);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            batches.Add([.. batch]);
        }

        return batches;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        bool inQuotes = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];

            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == ',' && !inQuotes)
            {
                fields.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        if (inQuotes)
        {
            throw new EtwRecordingException("The CSV recording has an unterminated quoted field.");
        }

        fields.Add(builder.ToString());
        return fields;
    }

    private sealed class JsonExportEvent
    {
        public string? Time { get; set; }
        public string? Provider { get; set; }
        public string? Event { get; set; }
        public ushort Id { get; set; }
        public byte Version { get; set; }
        public byte Opcode { get; set; }
        public byte Level { get; set; }
        public uint ProcessId { get; set; }
        public string? ProcessName { get; set; }
        public uint ThreadId { get; set; }
        public JsonExportParameter[]? Parameters { get; set; }
    }

    private sealed class JsonExportParameter
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Value { get; set; }
    }
}
