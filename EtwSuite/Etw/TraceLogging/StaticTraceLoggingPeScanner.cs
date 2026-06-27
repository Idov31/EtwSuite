using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;
using EtwSuite.Core;

namespace EtwSuite.Etw.TraceLogging;

public sealed class StaticTraceLoggingPeScanner : ITraceLoggingProviderScanner
{
    public const int ScannerVersion = 2;
    private const ulong DirectNearestWindowBytes = 0x200;

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
        ParsedMetadata parsedMetadata = ParseMetadataBlocks(
            metadata,
            fileOffsetBase: 0,
            metadataAddressBase: 0,
            sourcePath,
            diagnostics);

        return CreateResult(
            sourcePath,
            sourceLength: metadata.Length,
            sourceLastWriteTimeUtc: DateTimeOffset.UnixEpoch,
            providers: CreateProviderInfos(
                sourcePath,
                parsedMetadata.Providers,
                parsedMetadata.Events,
                ResolveEventOwnership(
                    sourcePath,
                    Machine.Amd64,
                    [],
                    parsedMetadata.Providers,
                    parsedMetadata.Events,
                    diagnostics),
                diagnostics),
            diagnostics: diagnostics);
    }

    public static TraceLoggingScanResult ParseTraceLoggingSectionsForTests(
        byte[] metadata,
        ulong metadataAddress,
        byte[] data,
        ulong dataAddress,
        byte[] code,
        ulong codeAddress,
        string sourcePath = "synthetic.bin")
    {
        var diagnostics = new List<TraceLoggingScanDiagnostic>();
        ParsedMetadata parsedMetadata = ParseMetadataBlocks(
            metadata,
            fileOffsetBase: 0,
            metadataAddressBase: metadataAddress,
            sourcePath,
            diagnostics);
        SectionInfo[] sections =
        [
            new SectionInfo(dataAddress, data, IsExecutable: false),
            new SectionInfo(codeAddress, code, IsExecutable: true),
        ];

        return CreateResult(
            sourcePath,
            sourceLength: metadata.Length + data.Length + code.Length,
            sourceLastWriteTimeUtc: DateTimeOffset.UnixEpoch,
            providers: CreateProviderInfos(
                sourcePath,
                parsedMetadata.Providers,
                parsedMetadata.Events,
                ResolveEventOwnership(
                    sourcePath,
                    Machine.Amd64,
                    sections,
                    parsedMetadata.Providers,
                    parsedMetadata.Events,
                    diagnostics),
                diagnostics),
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

            ulong imageBase = peReader.PEHeaders.PEHeader?.ImageBase ?? 0;
            var sections = new List<SectionInfo>();
            var parsedProviders = new List<ParsedProvider>();
            var parsedEvents = new List<ParsedEvent>();
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

                bool isExecutable = (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0;
                var sectionInfo = new SectionInfo(
                    imageBase + (uint)section.VirtualAddress,
                    sectionBytes,
                    isExecutable);
                sections.Add(sectionInfo);

                ParsedMetadata parsedMetadata = ParseMetadataBlocks(
                    sectionBytes,
                    section.PointerToRawData,
                    sectionInfo.Address,
                    filePath,
                    diagnostics);
                parsedProviders.AddRange(parsedMetadata.Providers);
                parsedEvents.AddRange(parsedMetadata.Events);
            }

            IReadOnlyDictionary<ParsedEvent, EventOwnership> ownership = ResolveEventOwnership(
                filePath,
                machine,
                sections,
                parsedProviders,
                parsedEvents,
                diagnostics);

            return CreateResult(
                filePath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                CreateProviderInfos(filePath, parsedProviders, parsedEvents, ownership, diagnostics),
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

    private static ParsedMetadata ParseMetadataBlocks(
        byte[] bytes,
        int fileOffsetBase,
        ulong metadataAddressBase,
        string sourcePath,
        List<TraceLoggingScanDiagnostic> diagnostics)
    {
        var providers = new List<ParsedProvider>();
        var events = new List<ParsedEvent>();

        for (int offset = 0; offset <= bytes.Length - 16; offset++)
        {
            if (!IsEtw0Header(bytes, offset))
            {
                continue;
            }

            ParseBlobs(
                bytes,
                offset + 16,
                fileOffsetBase,
                metadataAddressBase,
                sourcePath,
                providers,
                events,
                diagnostics);
        }

        return new ParsedMetadata(providers, events);
    }

    private static IReadOnlyList<TraceLoggingProviderInfo> CreateProviderInfos(
        string sourcePath,
        IReadOnlyList<ParsedProvider> providers,
        IReadOnlyList<ParsedEvent> events,
        IReadOnlyDictionary<ParsedEvent, EventOwnership> ownership,
        List<TraceLoggingScanDiagnostic> diagnostics)
    {
        if (providers.Count == 0)
        {
            return [];
        }

        bool hasSingleProvider = providers
            .Where(provider => provider.Id is not null)
            .Select(provider => provider.Id)
            .Distinct()
            .Count() == 1;

        if (!hasSingleProvider && events.Count > 0 && ownership.Count < events.Count)
        {
            int unresolvedCount = events.Count - ownership.Count;
            diagnostics.Add(new TraceLoggingScanDiagnostic(
                TraceLoggingDiagnosticSeverity.Info,
                string.Create(CultureInfo.InvariantCulture, $"{unresolvedCount:N0} TraceLogging event(s) could not be correlated to a provider and are not shown as provider-owned schema."),
                sourcePath));
        }

        return [.. providers.Select(provider => new TraceLoggingProviderInfo(
            provider.Name,
            provider.Id,
            provider.GroupId,
            sourcePath,
            [.. events
                .Where(schemaEvent =>
                    ownership.TryGetValue(schemaEvent, out EventOwnership? eventOwnership) &&
                    eventOwnership.Provider == provider)
                .Select(schemaEvent => schemaEvent.Schema with
                {
                    OwnershipConfidence = ownership[schemaEvent].Confidence
                })],
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
        ulong metadataAddressBase,
        string sourcePath,
        List<ParsedProvider> providers,
        List<ParsedEvent> events,
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
                if (!TryParseProvider(blobType, reader, metadataAddressBase, providers))
                {
                    diagnostics.Add(CreateMalformedDiagnostic(sourcePath, fileOffsetBase + blobStart, "provider"));
                    break;
                }

                unknownRun = 0;
                continue;
            }

            if (blobType is 3 or 5 or 6)
            {
                ParsedEvent? schemaEvent = TryParseEvent(blobType, reader, metadataAddressBase, sourcePath);
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
        ulong metadataAddressBase,
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

        ulong registrationAddress = metadataAddressBase + (uint)reader.Position;
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
            groupId,
            registrationAddress));
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

    private static ParsedEvent? TryParseEvent(
        byte blobType,
        BlobReader reader,
        ulong metadataAddressBase,
        string sourcePath)
    {
        ulong startAddress = metadataAddressBase + (uint)(reader.Position - 1);
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

        var schema = new TraceLoggingEventSchema(
            string.IsNullOrWhiteSpace(name) ? "(unnamed TraceLogging event)" : name,
            channel,
            level,
            opcode,
            keyword,
            fields,
            sourcePath);

        return new ParsedEvent(
            schema,
            startAddress,
            metadataAddressBase + (uint)end);
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

    private static IReadOnlyDictionary<ParsedEvent, EventOwnership> ResolveEventOwnership(
        string sourcePath,
        Machine machine,
        IReadOnlyList<SectionInfo> sections,
        IReadOnlyList<ParsedProvider> providers,
        IReadOnlyList<ParsedEvent> events,
        List<TraceLoggingScanDiagnostic> diagnostics)
    {
        var ownership = new Dictionary<ParsedEvent, EventOwnership>();
        ParsedProvider[] guidProviders = [.. providers.Where(provider => provider.Id is not null)];
        if (guidProviders.Select(provider => provider.Id).Distinct().Count() == 1)
        {
            ParsedProvider provider = guidProviders[0];
            foreach (ParsedEvent schemaEvent in events)
            {
                ownership[schemaEvent] = new EventOwnership(
                    provider,
                    TraceLoggingEventOwnershipConfidence.SingleProvider);
            }

            return ownership;
        }

        if (events.Count == 0 || providers.Count == 0)
        {
            return ownership;
        }

        if (machine != Machine.Amd64)
        {
            diagnostics.Add(new TraceLoggingScanDiagnostic(
                TraceLoggingDiagnosticSeverity.Info,
                $"TraceLogging event ownership resolution is not supported for {machine}; provider and event metadata were parsed without ownership.",
                sourcePath));
            return ownership;
        }

        Dictionary<ulong, ParsedProvider> providerAddressMap = BuildProviderAddressMap(sections, providers);
        if (providerAddressMap.Count == 0)
        {
            diagnostics.Add(new TraceLoggingScanDiagnostic(
                TraceLoggingDiagnosticSeverity.Info,
                "No TraceLogging provider runtime structures were found; multi-provider event ownership remains unresolved.",
                sourcePath));
            return ownership;
        }

        EventRange[] eventRanges = [.. events.Select(schemaEvent => new EventRange(schemaEvent))];
        foreach (SectionInfo section in sections.Where(section => section.IsExecutable))
        {
            IReadOnlyList<ProviderReference> providerReferences = FindProviderReferences(section, providerAddressMap);
            if (providerReferences.Count == 0)
            {
                continue;
            }

            foreach (EventReference eventReference in FindEventReferences(section, eventRanges))
            {
                ProviderReference? providerReference = providerReferences
                    .Where(candidate => candidate.InstructionAddress < eventReference.InstructionAddress)
                    .OrderByDescending(candidate => candidate.InstructionAddress)
                    .FirstOrDefault();
                TraceLoggingEventOwnershipConfidence confidence = TraceLoggingEventOwnershipConfidence.DirectPreceding;

                if (providerReference is null)
                {
                    providerReference = providerReferences
                        .Select(candidate => new
                        {
                            Reference = candidate,
                            Distance = Distance(candidate.InstructionAddress, eventReference.InstructionAddress)
                        })
                        .Where(candidate => candidate.Distance <= DirectNearestWindowBytes)
                        .OrderBy(candidate => candidate.Distance)
                        .Select(candidate => candidate.Reference)
                        .FirstOrDefault();
                    confidence = TraceLoggingEventOwnershipConfidence.DirectNearest;
                }

                if (providerReference is null)
                {
                    continue;
                }

                if (!ownership.TryGetValue(eventReference.Event, out EventOwnership? existing) ||
                    IsHigherConfidence(confidence, existing.Confidence))
                {
                    ownership[eventReference.Event] = new EventOwnership(providerReference.Provider, confidence);
                }
            }
        }

        return ownership;
    }

    private static Dictionary<ulong, ParsedProvider> BuildProviderAddressMap(
        IReadOnlyList<SectionInfo> sections,
        IReadOnlyList<ParsedProvider> providers)
    {
        var providerAddressMap = new Dictionary<ulong, ParsedProvider>();
        var registrationLookup = providers
            .Where(provider => provider.RegistrationAddress != 0)
            .GroupBy(provider => provider.RegistrationAddress)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (KeyValuePair<ulong, ParsedProvider> registration in registrationLookup)
        {
            providerAddressMap[registration.Key] = registration.Value;
        }

        var providerBases = new Dictionary<ulong, ParsedProvider>();
        foreach (SectionInfo section in sections.Where(section => !section.IsExecutable))
        {
            foreach ((ulong address, ulong value) in EnumerateQwords(section))
            {
                if (!registrationLookup.TryGetValue(value, out ParsedProvider? provider))
                {
                    continue;
                }

                ulong providerBase = address >= 8 ? address - 8 : address;
                providerBases[providerBase] = provider;
                for (ulong offset = 0; offset < 64; offset += 8)
                {
                    providerAddressMap[providerBase + offset] = provider;
                }
            }
        }

        if (providerBases.Count == 0)
        {
            return providerAddressMap;
        }

        foreach (SectionInfo section in sections.Where(section => !section.IsExecutable))
        {
            foreach ((ulong address, ulong value) in EnumerateQwords(section))
            {
                if (providerAddressMap.ContainsKey(address))
                {
                    continue;
                }

                if (providerBases.TryGetValue(value, out ParsedProvider? provider))
                {
                    providerAddressMap[address] = provider;
                }
            }
        }

        return providerAddressMap;
    }

    private static IEnumerable<(ulong Address, ulong Value)> EnumerateQwords(SectionInfo section)
    {
        for (int offset = 0; offset <= section.Bytes.Length - sizeof(ulong); offset += sizeof(ulong))
        {
            yield return (section.Address + (uint)offset, BitConverter.ToUInt64(section.Bytes, offset));
        }
    }

    private static IReadOnlyList<ProviderReference> FindProviderReferences(
        SectionInfo section,
        IReadOnlyDictionary<ulong, ParsedProvider> providerAddressMap)
    {
        var references = new List<ProviderReference>();
        foreach (CodeReference codeReference in FindCodeReferences(section))
        {
            if (providerAddressMap.TryGetValue(codeReference.TargetAddress, out ParsedProvider? provider))
            {
                references.Add(new ProviderReference(codeReference.InstructionAddress, provider));
            }
        }

        return references;
    }

    private static IReadOnlyList<EventReference> FindEventReferences(
        SectionInfo section,
        IReadOnlyList<EventRange> eventRanges)
    {
        var references = new List<EventReference>();
        foreach (CodeReference codeReference in FindCodeReferences(section))
        {
            ParsedEvent? schemaEvent = eventRanges
                .FirstOrDefault(range =>
                    codeReference.TargetAddress >= range.StartAddress &&
                    codeReference.TargetAddress < range.EndAddress)
                ?.Event;
            if (schemaEvent is not null)
            {
                references.Add(new EventReference(codeReference.InstructionAddress, schemaEvent));
            }
        }

        return references;
    }

    private static IEnumerable<CodeReference> FindCodeReferences(SectionInfo section)
    {
        byte[] bytes = section.Bytes;
        for (int offset = 0; offset < bytes.Length; offset++)
        {
            int instructionOffset = offset;
            byte first = bytes[offset];
            byte? rex = null;
            if (first is >= 0x40 and <= 0x4F && offset + 1 < bytes.Length)
            {
                rex = first;
                offset++;
                first = bytes[offset];
            }

            if (first is >= 0xB8 and <= 0xBF &&
                offset + 8 < bytes.Length &&
                (rex is null || (rex.Value & 0x08) != 0))
            {
                yield return new CodeReference(
                    section.Address + (uint)instructionOffset,
                    BitConverter.ToUInt64(bytes, offset + 1));
                offset += 8;
                continue;
            }

            if (first is not (0x8B or 0x8D) ||
                offset + 5 >= bytes.Length)
            {
                continue;
            }

            byte modRm = bytes[offset + 1];
            if ((modRm & 0xC7) != 0x05)
            {
                continue;
            }

            int displacement = BitConverter.ToInt32(bytes, offset + 2);
            ulong nextInstruction = section.Address + (uint)(offset + 6);
            yield return new CodeReference(
                section.Address + (uint)instructionOffset,
                unchecked((ulong)((long)nextInstruction + displacement)));
            offset += 5;
        }
    }

    private static bool IsHigherConfidence(
        TraceLoggingEventOwnershipConfidence candidate,
        TraceLoggingEventOwnershipConfidence existing)
    {
        return Rank(candidate) < Rank(existing);

        static int Rank(TraceLoggingEventOwnershipConfidence confidence)
        {
            return confidence switch
            {
                TraceLoggingEventOwnershipConfidence.SingleProvider => 0,
                TraceLoggingEventOwnershipConfidence.DirectPreceding => 1,
                TraceLoggingEventOwnershipConfidence.DirectNearest => 2,
                _ => 3
            };
        }
    }

    private static ulong Distance(ulong left, ulong right)
    {
        return left >= right ? left - right : right - left;
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

    private sealed record SectionInfo(ulong Address, byte[] Bytes, bool IsExecutable);

    private sealed record ParsedMetadata(
        IReadOnlyList<ParsedProvider> Providers,
        IReadOnlyList<ParsedEvent> Events);

    private sealed record ParsedProvider(
        string Name,
        Guid? Id,
        Guid? GroupId,
        ulong RegistrationAddress);

    private sealed record ParsedEvent(
        TraceLoggingEventSchema Schema,
        ulong StartAddress,
        ulong EndAddress);

    private sealed record EventOwnership(
        ParsedProvider Provider,
        TraceLoggingEventOwnershipConfidence Confidence);

    private sealed record EventRange(ParsedEvent Event)
    {
        public ulong StartAddress => Event.StartAddress;

        public ulong EndAddress => Event.EndAddress;
    }

    private sealed record CodeReference(ulong InstructionAddress, ulong TargetAddress);

    private sealed record ProviderReference(ulong InstructionAddress, ParsedProvider Provider);

    private sealed record EventReference(ulong InstructionAddress, ParsedEvent Event);

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
