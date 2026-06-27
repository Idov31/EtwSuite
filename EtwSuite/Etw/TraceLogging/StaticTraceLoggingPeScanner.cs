using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;
using EtwSuite.Core;

namespace EtwSuite.Etw.TraceLogging;

public sealed class StaticTraceLoggingPeScanner : ITraceLoggingProviderScanner
{
    public const int ScannerVersion = 1;

    private static readonly byte[] Etw0Signature = "ETW0"u8.ToArray();
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".dll",
        ".sys",
    };

    private readonly ITraceLoggingProviderCache _cache;

    public StaticTraceLoggingPeScanner(ITraceLoggingProviderCache cache)
    {
        _cache = cache;
    }

    public async Task<TraceLoggingScanResult> ScanAsync(
        IReadOnlyList<TraceLoggingScanPath> paths,
        bool useCache,
        CancellationToken cancellationToken)
    {
        if (useCache)
        {
            TraceLoggingScanResult cached = await _cache.LoadCachedResultAsync(cancellationToken);
            if (IsCacheCurrent(cached))
            {
                return cached;
            }
        }

        return await Task.Run(() => Scan(paths, cancellationToken), cancellationToken);
    }

    public static TraceLoggingScanResult ParseTraceLoggingMetadataForTests(
        byte[] metadata,
        string sourcePath = "synthetic.bin")
    {
        var diagnostics = new List<TraceLoggingScanDiagnostic>();
        return CreateResult(
            sourcePath,
            sourceLength: metadata.Length,
            sourceLastWriteTimeUtc: DateTimeOffset.UnixEpoch,
            providers: ParseMetadataBlocks(metadata, 0, sourcePath, diagnostics),
            diagnostics: diagnostics);
    }

    private static TraceLoggingScanResult Scan(
        IReadOnlyList<TraceLoggingScanPath> paths,
        CancellationToken cancellationToken)
    {
        var providers = new List<TraceLoggingProviderInfo>();
        var diagnostics = new List<TraceLoggingScanDiagnostic>();

        foreach (string filePath in ExpandPaths(paths, diagnostics, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraceLoggingScanResult fileResult = ScanFile(filePath, cancellationToken);
            providers.AddRange(fileResult.Providers);
            diagnostics.AddRange(fileResult.Diagnostics);
        }

        return new TraceLoggingScanResult(
            DeduplicateProviders(providers),
            [.. diagnostics],
            ScannerVersion);
    }

    private static IEnumerable<string> ExpandPaths(
        IReadOnlyList<TraceLoggingScanPath> paths,
        List<TraceLoggingScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (TraceLoggingScanPath path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Error,
                    $"Invalid path: {ex.Message}",
                    path.Path));
                continue;
            }

            if (path.Kind == TraceLoggingScanPathKind.File)
            {
                if (File.Exists(fullPath))
                {
                    yield return fullPath;
                }
                else
                {
                    diagnostics.Add(new TraceLoggingScanDiagnostic(
                        TraceLoggingDiagnosticSeverity.Error,
                        "File does not exist.",
                        fullPath));
                }

                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Error,
                    "Folder does not exist.",
                    fullPath));
                continue;
            }

            IEnumerator<string>? files = null;
            try
            {
                files = Directory
                    .EnumerateFiles(fullPath, "*.*", SearchOption.AllDirectories)
                    .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)))
                    .GetEnumerator();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Error,
                    $"Folder enumeration failed: {ex.Message}",
                    fullPath));
            }

            if (files is null)
            {
                continue;
            }

            using (files)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string file;
                    try
                    {
                        if (!files.MoveNext())
                        {
                            break;
                        }

                        file = files.Current;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        diagnostics.Add(new TraceLoggingScanDiagnostic(
                            TraceLoggingDiagnosticSeverity.Warning,
                            $"Folder enumeration skipped an entry: {ex.Message}",
                            fullPath));
                        break;
                    }

                    yield return file;
                }
            }
        }
    }

    private static TraceLoggingScanResult ScanFile(string filePath, CancellationToken cancellationToken)
    {
        var diagnostics = new List<TraceLoggingScanDiagnostic>();
        FileInfo fileInfo;

        try
        {
            fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return new TraceLoggingScanResult(
                    [],
                    [new TraceLoggingScanDiagnostic(TraceLoggingDiagnosticSeverity.Error, "File does not exist.", filePath)]);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new TraceLoggingScanResult(
                [],
                [new TraceLoggingScanDiagnostic(TraceLoggingDiagnosticSeverity.Error, $"File metadata failed: {ex.Message}", filePath)]);
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata && peReader.PEHeaders.PEHeader is null)
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Warning,
                    "File is not a valid PE image.",
                    filePath));
                return new TraceLoggingScanResult([], diagnostics);
            }

            Machine machine = peReader.PEHeaders.CoffHeader.Machine;
            if (machine is not (Machine.Amd64 or Machine.Arm64 or Machine.I386))
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Warning,
                    $"Unsupported PE machine type: {machine}.",
                    filePath));
            }

            var providers = new List<TraceLoggingProviderInfo>();
            foreach (SectionHeader section in peReader.PEHeaders.SectionHeaders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (section.PointerToRawData <= 0 || section.SizeOfRawData <= 0)
                {
                    continue;
                }

                if (section.PointerToRawData >= stream.Length)
                {
                    continue;
                }

                int bytesToRead = checked((int)Math.Min(section.SizeOfRawData, stream.Length - section.PointerToRawData));
                byte[] sectionBytes = new byte[bytesToRead];
                stream.Position = section.PointerToRawData;
                int read = stream.Read(sectionBytes, 0, sectionBytes.Length);
                if (read != sectionBytes.Length)
                {
                    Array.Resize(ref sectionBytes, read);
                }

                providers.AddRange(ParseMetadataBlocks(
                    sectionBytes,
                    section.PointerToRawData,
                    filePath,
                    diagnostics));
            }

            return CreateResult(
                filePath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                providers,
                diagnostics);
        }
        catch (BadImageFormatException ex)
        {
            return new TraceLoggingScanResult(
                [],
                [new TraceLoggingScanDiagnostic(TraceLoggingDiagnosticSeverity.Warning, $"Invalid PE image: {ex.Message}", filePath)]);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new TraceLoggingScanResult(
                [],
                [new TraceLoggingScanDiagnostic(TraceLoggingDiagnosticSeverity.Error, $"File scan failed: {ex.Message}", filePath)]);
        }
    }

    private static IReadOnlyList<TraceLoggingProviderInfo> ParseMetadataBlocks(
        byte[] bytes,
        int fileOffsetBase,
        string sourcePath,
        List<TraceLoggingScanDiagnostic> diagnostics)
    {
        var providers = new List<ParsedProvider>();
        var events = new List<TraceLoggingEventSchema>();

        for (int offset = 0; offset <= bytes.Length - 16; offset++)
        {
            if (!IsEtw0Header(bytes, offset))
            {
                continue;
            }

            ParseBlobs(bytes, offset + 16, fileOffsetBase, sourcePath, providers, events, diagnostics);
        }

        if (providers.Count == 0)
        {
            return [];
        }

        bool hasSingleProvider = providers
            .Where(provider => provider.Id is not null)
            .Select(provider => provider.Id)
            .Distinct()
            .Count() == 1;

        if (!hasSingleProvider && events.Count > 0)
        {
            diagnostics.Add(new TraceLoggingScanDiagnostic(
                TraceLoggingDiagnosticSeverity.Info,
                "Provider/event ownership is unresolved without call/reference analysis; showing all TraceLogging events found in this binary for each provider.",
                sourcePath));
        }

        return [.. providers.Select(provider => new TraceLoggingProviderInfo(
            provider.Name,
            provider.Id,
            provider.GroupId,
            sourcePath,
            events,
            [],
            FromCache: false,
            SourceLength: 0,
            SourceLastWriteTimeUtc: default))];
    }

    private static TraceLoggingScanResult CreateResult(
        string sourcePath,
        long sourceLength,
        DateTimeOffset sourceLastWriteTimeUtc,
        IReadOnlyList<TraceLoggingProviderInfo> providers,
        IReadOnlyList<TraceLoggingScanDiagnostic> diagnostics)
    {
        return new TraceLoggingScanResult(
            [.. providers.Select(provider => provider with
            {
                SourcePath = sourcePath,
                SourceLength = sourceLength,
                SourceLastWriteTimeUtc = sourceLastWriteTimeUtc,
                FromCache = false
            })],
            diagnostics,
            ScannerVersion);
    }

    private static bool IsEtw0Header(byte[] bytes, int offset)
    {
        return bytes.AsSpan(offset, 4).SequenceEqual(Etw0Signature) &&
            BitConverter.ToUInt16(bytes, offset + 4) == 16 &&
            BitConverter.ToUInt64(bytes, offset + 8) == 0xBB8A052B88040E86UL;
    }

    private static void ParseBlobs(
        byte[] bytes,
        int startOffset,
        int fileOffsetBase,
        string sourcePath,
        List<ParsedProvider> providers,
        List<TraceLoggingEventSchema> events,
        List<TraceLoggingScanDiagnostic> diagnostics)
    {
        var reader = new BlobReader(bytes, startOffset);
        int unknownRun = 0;

        while (reader.Position < bytes.Length)
        {
            int blobStart = reader.Position;
            if (!reader.TryReadByte(out byte blobType))
            {
                break;
            }

            if (blobType == 1)
            {
                break;
            }

            if (blobType == 0)
            {
                unknownRun = 0;
                continue;
            }

            if (blobType is 2 or 4)
            {
                if (!TryParseProvider(blobType, reader, providers))
                {
                    diagnostics.Add(CreateMalformedDiagnostic(sourcePath, fileOffsetBase + blobStart, "provider"));
                    break;
                }

                unknownRun = 0;
                continue;
            }

            if (blobType is 3 or 5 or 6)
            {
                TraceLoggingEventSchema? schemaEvent = TryParseEvent(blobType, reader, sourcePath);
                if (schemaEvent is null)
                {
                    diagnostics.Add(CreateMalformedDiagnostic(sourcePath, fileOffsetBase + blobStart, "event"));
                    break;
                }

                events.Add(schemaEvent);
                unknownRun = 0;
                continue;
            }

            unknownRun++;
            if (unknownRun > 128)
            {
                diagnostics.Add(new TraceLoggingScanDiagnostic(
                    TraceLoggingDiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"Stopping TraceLogging blob parse after 128 unknown bytes near file offset 0x{fileOffsetBase + reader.Position:X}."),
                    sourcePath));
                break;
            }
        }
    }

    private static bool TryParseProvider(
        byte blobType,
        BlobReader reader,
        List<ParsedProvider> providers)
    {
        Guid? providerId = null;
        if (blobType == 4)
        {
            if (!reader.TryReadGuid(out Guid id))
            {
                return false;
            }

            providerId = id;
        }

        if (!reader.TryReadUInt16(out ushort remaining) || remaining < 2)
        {
            return false;
        }

        int end = reader.Position + remaining - 2;
        if (end < reader.Position || end > reader.Length)
        {
            return false;
        }

        string? name = reader.TryReadCString(end);
        Guid? groupId = TryParseProviderTraits(reader, end);
        reader.Position = end;

        providers.Add(new ParsedProvider(
            string.IsNullOrWhiteSpace(name) ? "(unnamed TraceLogging provider)" : name,
            providerId,
            groupId));
        return true;
    }

    private static Guid? TryParseProviderTraits(BlobReader reader, int end)
    {
        if (reader.Position >= end || !reader.TryReadUInt16(out ushort chunkSize) || !reader.TryReadByte(out byte chunkType))
        {
            return null;
        }

        if (chunkType == 1 && chunkSize >= 19 && reader.TryReadGuid(out Guid groupId))
        {
            return groupId;
        }

        reader.Position = Math.Min(end, reader.Position + Math.Max(0, chunkSize - 3));
        return null;
    }

    private static TraceLoggingEventSchema? TryParseEvent(byte blobType, BlobReader reader, string sourcePath)
    {
        byte channel;
        byte level;
        byte opcode;
        ulong keyword;
        ushort remaining;

        if (blobType is 3 or 6)
        {
            if (!reader.TryReadByte(out channel) ||
                !reader.TryReadByte(out level) ||
                !reader.TryReadByte(out opcode) ||
                !reader.TryReadUInt64(out keyword) ||
                !reader.TryReadUInt16(out remaining))
            {
                return null;
            }
        }
        else
        {
            channel = 0x0B;
            if (!reader.TryReadByte(out level) ||
                !reader.TryReadByte(out opcode) ||
                !reader.TryReadUInt16(out _) ||
                !reader.TryReadUInt64(out keyword) ||
                !reader.TryReadUInt16(out remaining))
            {
                return null;
            }
        }

        if (remaining < 2)
        {
            return null;
        }

        int end = reader.Position + remaining - 2;
        if (end < reader.Position || end > reader.Length)
        {
            return null;
        }

        for (int i = 0; i < 4 && reader.Position < end; i++)
        {
            if (!reader.TryReadByte(out byte value))
            {
                return null;
            }

            if ((value & 0x80) == 0)
            {
                break;
            }
        }

        string? name = reader.TryReadCString(end);
        IReadOnlyList<EtwSchemaParameter> fields = ParseEventFields(reader, end);
        reader.Position = end;

        return new TraceLoggingEventSchema(
            string.IsNullOrWhiteSpace(name) ? "(unnamed TraceLogging event)" : name,
            channel,
            level,
            opcode,
            keyword,
            fields,
            sourcePath);
    }

    private static IReadOnlyList<EtwSchemaParameter> ParseEventFields(BlobReader reader, int end)
    {
        var fields = new List<EtwSchemaParameter>();
        while (reader.Position < end)
        {
            string? fieldName = reader.TryReadCString(end);
            if (string.IsNullOrWhiteSpace(fieldName) || !reader.TryReadByte(out byte inputValue))
            {
                break;
            }

            int inputType = inputValue & 0x1F;
            string arraySuffix = (inputValue & 0x40) != 0
                ? "[]"
                : (inputValue & 0x20) != 0 ? "[N]" : string.Empty;
            string outputType = string.Empty;

            if ((inputValue & 0x80) != 0)
            {
                if (!reader.TryReadByte(out byte outputValue))
                {
                    break;
                }

                outputType = MapTraceLoggingOutputType(outputValue & 0x7F);
                if ((outputValue & 0x80) != 0)
                {
                    for (int i = 0; i < 4 && reader.Position < end; i++)
                    {
                        if (!reader.TryReadByte(out byte extension) || (extension & 0x80) == 0)
                        {
                            break;
                        }
                    }
                }
            }

            if ((inputValue & 0x60) == 0x20)
            {
                reader.TryReadUInt16(out _);
            }
            else if ((inputValue & 0x60) == 0x60 && reader.TryReadUInt16(out ushort typeSize))
            {
                reader.Skip(typeSize);
            }

            string typeName = MapTraceLoggingInputType(inputType) + arraySuffix;
            if (!string.IsNullOrWhiteSpace(outputType))
            {
                typeName += $"({outputType})";
            }

            fields.Add(new EtwSchemaParameter(fieldName, typeName));
        }

        return fields;
    }

    private static IReadOnlyList<TraceLoggingProviderInfo> DeduplicateProviders(
        IReadOnlyList<TraceLoggingProviderInfo> providers)
    {
        return [.. providers
            .GroupBy(provider => new
            {
                provider.Id,
                Name = provider.Name.ToUpperInvariant(),
                SourcePath = provider.SourcePath.ToUpperInvariant()
            })
            .Select(group =>
            {
                TraceLoggingProviderInfo first = group.First();
                IReadOnlyList<TraceLoggingEventSchema> events = [.. group
                    .SelectMany(provider => provider.Events)
                    .GroupBy(schemaEvent => new
                    {
                        schemaEvent.Name,
                        schemaEvent.Level,
                        schemaEvent.Opcode,
                        schemaEvent.Keyword
                    })
                    .Select(eventGroup => eventGroup.First())
                    .OrderBy(schemaEvent => schemaEvent.Name, StringComparer.CurrentCultureIgnoreCase)];

                return first with { Events = events };
            })
            .OrderBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(provider => provider.Id)];
    }

    private static bool IsCacheCurrent(TraceLoggingScanResult cached)
    {
        if (cached.Providers.Count == 0)
        {
            return false;
        }

        if (cached.ScannerVersion != ScannerVersion)
        {
            return false;
        }

        foreach (TraceLoggingProviderInfo provider in cached.Providers)
        {
            if (provider.SourceLength <= 0 || provider.SourceLastWriteTimeUtc == default)
            {
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(provider.SourcePath);
                if (!fileInfo.Exists ||
                    fileInfo.Length != provider.SourceLength ||
                    fileInfo.LastWriteTimeUtc != provider.SourceLastWriteTimeUtc)
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }

        return true;
    }

    private static TraceLoggingScanDiagnostic CreateMalformedDiagnostic(
        string sourcePath,
        int fileOffset,
        string blobKind)
    {
        return new TraceLoggingScanDiagnostic(
            TraceLoggingDiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"Malformed TraceLogging {blobKind} blob near file offset 0x{fileOffset:X}."),
            sourcePath);
    }

    private static string MapTraceLoggingInputType(int inputType)
    {
        return inputType switch
        {
            0 => "Null",
            1 => "UnicodeString",
            2 => "AnsiString",
            3 => "Int8",
            4 => "UInt8",
            5 => "Int16",
            6 => "UInt16",
            7 => "Int32",
            8 => "UInt32",
            9 => "Int64",
            10 => "UInt64",
            11 => "Float",
            12 => "Double",
            13 => "Bool32",
            14 => "Binary",
            15 => "Guid",
            16 => "Pointer",
            17 => "FileTime",
            18 => "SystemTime",
            19 => "Sid",
            20 => "HexInt32",
            21 => "HexInt64",
            22 => "CountedString",
            23 => "CountedAnsiString",
            24 => "Struct",
            25 => "CountedBinary",
            _ => inputType.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string MapTraceLoggingOutputType(int outputType)
    {
        return outputType switch
        {
            0 => string.Empty,
            1 => "NoPrint",
            2 => "String",
            3 => "Boolean",
            4 => "Hex",
            5 => "PID",
            6 => "TID",
            7 => "Port",
            8 => "IPv4",
            9 => "IPv6",
            10 => "SocketAddress",
            11 => "XML",
            12 => "JSON",
            13 => "Win32Error",
            14 => "NTStatus",
            15 => "HResult",
            16 => "FileTime",
            17 => "Signed",
            18 => "Unsigned",
            35 => "UTF8",
            36 => "PKCS7",
            37 => "CodePointer",
            38 => "DateTimeUTC",
            _ => outputType.ToString(CultureInfo.InvariantCulture)
        };
    }

    private sealed record ParsedProvider(string Name, Guid? Id, Guid? GroupId);

    private sealed class BlobReader
    {
        private readonly byte[] _bytes;

        public BlobReader(byte[] bytes, int position)
        {
            _bytes = bytes;
            Position = position;
        }

        public int Position { get; set; }

        public int Length => _bytes.Length;

        public bool TryReadByte(out byte value)
        {
            if (Position >= _bytes.Length)
            {
                value = 0;
                return false;
            }

            value = _bytes[Position++];
            return true;
        }

        public bool TryReadUInt16(out ushort value)
        {
            if (Position + sizeof(ushort) > _bytes.Length)
            {
                value = 0;
                return false;
            }

            value = BitConverter.ToUInt16(_bytes, Position);
            Position += sizeof(ushort);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (Position + sizeof(ulong) > _bytes.Length)
            {
                value = 0;
                return false;
            }

            value = BitConverter.ToUInt64(_bytes, Position);
            Position += sizeof(ulong);
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            if (Position + 16 > _bytes.Length)
            {
                value = Guid.Empty;
                return false;
            }

            value = new Guid(_bytes.AsSpan(Position, 16));
            Position += 16;
            return true;
        }

        public string? TryReadCString(int end)
        {
            int boundedEnd = Math.Min(end, _bytes.Length);
            int start = Position;
            while (Position < boundedEnd)
            {
                if (_bytes[Position++] == 0)
                {
                    int length = Position - start - 1;
                    return length == 0 ? string.Empty : Encoding.UTF8.GetString(_bytes, start, length);
                }
            }

            return null;
        }

        public void Skip(int count)
        {
            Position = Math.Min(_bytes.Length, Math.Max(Position, Position + count));
        }
    }
}
