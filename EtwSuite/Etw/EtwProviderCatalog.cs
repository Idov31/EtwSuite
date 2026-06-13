using EtwSuite.Core;
using EtwSuite.Etw.Native;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace EtwSuite.Etw;

public sealed class EtwProviderEnumerationException(uint errorCode) : Exception($"Failed to enumerate ETW providers. TDH returned Win32 error {errorCode}.")
{
    public uint ErrorCode { get; } = errorCode;
}


public sealed class EtwProviderCatalog : IEtwProviderCatalog
{
    public Task<IReadOnlyList<EtwProviderInfo>> EnumerateProvidersAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => EnumerateProviders(cancellationToken), cancellationToken);
    }

    public Task<EtwProviderSchema> GetProviderSchemaAsync(
        EtwProviderInfo provider,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => GetProviderSchema(provider, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<EtwProviderInfo> EnumerateProviders(CancellationToken cancellationToken)
    {
        uint bufferSize = 0;
        uint result = TdhNative.TdhEnumerateProviders(IntPtr.Zero, ref bufferSize);

        if (result != TdhNative.ErrorInsufficientBuffer && result != TdhNative.ErrorSuccess)
        {
            throw new EtwProviderEnumerationException(result);
        }

        if (bufferSize == 0)
        {
            return [];
        }
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));

        try
        {
            result = TdhNative.TdhEnumerateProviders(buffer, ref bufferSize);

            if (result != TdhNative.ErrorSuccess)
            {
                throw new EtwProviderEnumerationException(result);
            }
            var header = Marshal.PtrToStructure<TdhNative.ProviderEnumerationInfoHeader>(buffer);

            if (header.NumberOfProviders == 0)
            {
                Marshal.FreeHGlobal(buffer);
                return [];
            }
            var providers = new List<EtwProviderInfo>(checked((int)header.NumberOfProviders));
            int headerSize = Marshal.SizeOf<TdhNative.ProviderEnumerationInfoHeader>();
            int providerInfoSize = Marshal.SizeOf<TdhNative.TraceProviderInfo>();

            for (int i = 0; i < header.NumberOfProviders; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr providerInfoAddress = IntPtr.Add(buffer, headerSize + (i * providerInfoSize));
                var providerInfo = Marshal.PtrToStructure<TdhNative.TraceProviderInfo>(providerInfoAddress);
                string name = ReadProviderName(buffer, providerInfo.ProviderNameOffset);

                providers.Add(new EtwProviderInfo(
                    name,
                    providerInfo.ProviderGuid,
                    MapSchemaSource(providerInfo.SchemaSource)));
            }

            return [.. providers
                .OrderBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(provider => provider.Id)];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ReadProviderName(IntPtr buffer, uint providerNameOffset)
    {
        if (buffer == IntPtr.Zero || providerNameOffset == 0)
        {
            return "(unknown provider)";
        }

        IntPtr providerNameAddress = IntPtr.Add(buffer, checked((int)providerNameOffset));
        return Marshal.PtrToStringUni(providerNameAddress) ?? "(unknown provider)";
    }

    private static EtwProviderSchemaSource MapSchemaSource(uint schemaSource)
    {
        return Enum.IsDefined(typeof(EtwProviderSchemaSource), (int)schemaSource)
            ? (EtwProviderSchemaSource)schemaSource
            : EtwProviderSchemaSource.Unknown;
    }

    private static EtwProviderSchema GetProviderSchema(
        EtwProviderInfo provider,
        CancellationToken cancellationToken)
    {
        if (provider.SchemaSource == EtwProviderSchemaSource.Unknown)
        {
            return new EtwProviderSchema(
                provider,
                [],
                ["The provider schema source is unknown, so static event metadata is not available."]);
        }
        var diagnostics = new List<string>();
        EtwProviderSchema? manifestSchema = TryGetRegisteredManifestSchema(provider, diagnostics);

        if (manifestSchema is not null)
        {
            return manifestSchema;
        }
        EtwProviderSchema? wmiSchema = TryGetWmiSchema(provider, diagnostics, cancellationToken);

        if (wmiSchema is not null)
        {
            return wmiSchema;
        }

        if (provider.SchemaSource is EtwProviderSchemaSource.Wpp or EtwProviderSchemaSource.TraceLogging)
        {
            diagnostics.Add($"{FormatSchemaSource(provider.SchemaSource)} providers usually do not expose a complete registered XML manifest. Event templates may require live event samples or external TMF/TraceLogging metadata.");
            return new EtwProviderSchema(provider, [], [.. diagnostics.Distinct(StringComparer.Ordinal)]);
        }
        List<TdhNative.EventDescriptor> eventDescriptors = EnumerateEventDescriptors(provider, diagnostics);

        if (eventDescriptors.Count == 0)
        {
            diagnostics.Add($"No static {FormatSchemaSource(provider.SchemaSource)} events were exposed by the registered manifest or TDH for this provider.");
        }
        var events = new List<EtwSchemaEvent>(eventDescriptors.Count);

        foreach (var eventDescriptor in eventDescriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EtwSchemaEvent? schemaEvent = TryReadEventInformation(provider, eventDescriptor, diagnostics);

            if (schemaEvent is not null)
            {
                events.Add(schemaEvent);
            }
        }

        return new EtwProviderSchema(
            provider,
            [.. events
                .OrderBy(schemaEvent => schemaEvent.Id)
                .ThenBy(schemaEvent => schemaEvent.Version)
                .ThenBy(schemaEvent => schemaEvent.Name, StringComparer.CurrentCultureIgnoreCase)],
            [.. diagnostics.Distinct(StringComparer.Ordinal)]);
    }

    private static EtwProviderSchema? TryGetRegisteredManifestSchema(
        EtwProviderInfo provider,
        List<string> diagnostics)
    {
        string? manifestXml;

        try
        {
            manifestXml = RegisteredTraceEventParser.GetManifestForRegisteredProvider(provider.Id);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Registered manifest lookup failed: {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifestXml))
        {
            return null;
        }

        try
        {
            EtwProviderSchema schema = ParseRegisteredManifest(provider, manifestXml);

            if (schema.Events.Count == 0)
            {
                diagnostics.Add("Registered manifest was found, but no event elements were parsed from it.");
                return new EtwProviderSchema(provider, schema.Events, [.. diagnostics.Distinct(StringComparer.Ordinal)]);
            }

            return schema;
        }
        catch (XmlException ex)
        {
            diagnostics.Add($"Registered manifest XML could not be parsed: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add($"Registered manifest XML could not be interpreted: {ex.Message}");
            return null;
        }
    }

    private static EtwProviderSchema ParseRegisteredManifest(
        EtwProviderInfo provider,
        string manifestXml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var stringReader = new StringReader(manifestXml);
        using XmlReader reader = XmlReader.Create(stringReader, settings);
        XDocument manifest = XDocument.Load(reader, LoadOptions.None);
        XElement? providerElement = FindProviderElement(manifest, provider);

        if (providerElement is null)
        {
            return new EtwProviderSchema(
                provider,
                [],
                ["Registered manifest did not contain a provider element."]);
        }
        Dictionary<string, string> tasksByValue = ReadValueNameMap(providerElement, "tasks", "task");
        Dictionary<string, string> opcodesByValue = ReadValueNameMap(providerElement, "opcodes", "opcode");
        Dictionary<string, string> levelsByValue = ReadValueNameMap(providerElement, "levels", "level");
        Dictionary<string, IReadOnlyList<EtwSchemaParameter>> templates = ReadTemplates(providerElement);

        if (templates.Count == 0)
        {
            templates = ReadTemplates(manifest.Root ?? providerElement);
        }

        var events = new List<EtwSchemaEvent>();
        IEnumerable<XElement> eventElements = providerElement
            .Descendants()
            .Where(element => element.Name.LocalName == "event");

        if (!eventElements.Any())
        {
            eventElements = manifest
                .Descendants()
                .Where(element => element.Name.LocalName == "event");
        }

        foreach (XElement eventElement in eventElements)
        {
            string idText = ReadAttribute(eventElement, "value", "0");
            ushort id = ushort.TryParse(idText, out ushort parsedId) ? parsedId : (ushort)0;
            string versionText = ReadAttribute(eventElement, "version", "0");
            byte version = byte.TryParse(versionText, out byte parsedVersion) ? parsedVersion : (byte)0;
            string task = ResolveReference(ReadAttribute(eventElement, "task", string.Empty), tasksByValue);
            string opcode = ResolveReference(ReadAttribute(eventElement, "opcode", string.Empty), opcodesByValue);
            string level = ResolveReference(ReadAttribute(eventElement, "level", string.Empty), levelsByValue);
            string templateId = ReadAttribute(eventElement, "template", string.Empty);
            templates.TryGetValue(templateId, out IReadOnlyList<EtwSchemaParameter>? parameters);

            events.Add(new EtwSchemaEvent(
                GetManifestEventName(eventElement, task, id),
                id,
                version,
                string.IsNullOrWhiteSpace(opcode) ? "0" : NormalizeManifestReference(opcode),
                string.IsNullOrWhiteSpace(level) ? "0" : NormalizeManifestReference(level),
                parameters ?? []));
        }

        return new EtwProviderSchema(provider, events, []);
    }

    private static XElement? FindProviderElement(XDocument manifest, EtwProviderInfo provider)
    {
        return manifest
            .Descendants()
            .Where(element => element.Name.LocalName == "provider")
            .FirstOrDefault(element =>
                string.Equals(ReadAttribute(element, "guid", string.Empty).Trim('{', '}'), provider.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ReadAttribute(element, "name", string.Empty), provider.Name, StringComparison.OrdinalIgnoreCase))
            ?? manifest
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "provider");
    }

    private static Dictionary<string, string> ReadValueNameMap(
        XElement providerElement,
        string containerName,
        string itemName)
    {
        return providerElement
            .Descendants()
            .Where(element => element.Name.LocalName == containerName)
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => new
            {
                Value = ReadAttribute(element, "value", string.Empty),
                Name = ReadAttribute(element, "name", ReadAttribute(element, "symbol", string.Empty))
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value) && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<EtwSchemaParameter>> ReadTemplates(XElement providerElement)
    {
        return providerElement
            .Descendants()
            .Where(element => element.Name.LocalName == "template")
            .Select(template => new
            {
                Id = ReadAttribute(template, "tid", string.Empty),
                Parameters = template
                    .Elements()
                    .Where(element => element.Name.LocalName is "data" or "struct")
                    .Select(element => new EtwSchemaParameter(
                        ReadAttribute(element, "name", "(unnamed parameter)"),
                        MapManifestInputType(ReadAttribute(element, "inType", element.Name.LocalName))))
                    .ToArray()
            })
            .Where(template => !string.IsNullOrWhiteSpace(template.Id))
            .GroupBy(template => template.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<EtwSchemaParameter>)group.First().Parameters, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetManifestEventName(XElement eventElement, string task, ushort id)
    {
        string symbol = ReadAttribute(eventElement, "symbol", string.Empty);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            return symbol;
        }

        string message = ReadAttribute(eventElement, "message", string.Empty);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return string.IsNullOrWhiteSpace(task) ? $"Event {id}" : task;
    }

    private static string ResolveReference(string value, Dictionary<string, string> valueNameMap)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (valueNameMap.TryGetValue(value, out string? mappedValue))
        {
            return mappedValue;
        }

        return value;
    }

    private static string NormalizeManifestReference(string value)
    {
        return value.StartsWith("win:", StringComparison.OrdinalIgnoreCase)
            ? value[4..]
            : value;
    }

    private static string ReadAttribute(XElement element, string name, string fallback)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? fallback;
    }

    private static EtwProviderSchema? TryGetWmiSchema(
        EtwProviderInfo provider,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        EtwProviderSchema? schema = null;

        try
        {
            schema = GetWmiSchema(provider, cancellationToken);
        }
        catch (ManagementException ex)
        {
            diagnostics.Add($"WMI/MOF metadata lookup failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            diagnostics.Add($"WMI/MOF metadata access denied: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add($"WMI/MOF metadata lookup failed: {ex.Message}");
        }
        return schema;
    }

    private static EtwProviderSchema? GetWmiSchema(
        EtwProviderInfo provider,
        CancellationToken cancellationToken)
    {
        ManagementClass? providerClass = FindWmiProviderClass(provider.Id, cancellationToken);

        if (providerClass is null)
        {
            return null;
        }

        var events = new SortedDictionary<string, EtwSchemaEvent>(StringComparer.Ordinal);
        string providerClassName = Convert.ToString(providerClass["__CLASS"], CultureInfo.InvariantCulture) ?? string.Empty;
        using var categorySearcher = new ManagementObjectSearcher(
            "root\\WMI",
            $"SELECT * FROM meta_class WHERE __superclass = '{providerClassName}'");

        foreach (ManagementClass categoryClass in categorySearcher.Get().OfType<ManagementClass>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            int categoryVersion = GetQualifierInt32(categoryClass, "eventversion") ?? 0;
            string category = GetQualifierString(categoryClass, "guid") ?? Convert.ToString(categoryClass["__CLASS"], CultureInfo.InvariantCulture) ?? string.Empty;
            string categoryClassName = Convert.ToString(categoryClass["__CLASS"], CultureInfo.InvariantCulture) ?? string.Empty;

            using var templateSearcher = new ManagementObjectSearcher(
                "root\\WMI",
                $"SELECT * FROM meta_class WHERE __superclass = '{categoryClassName}'");

            foreach (ManagementClass templateClass in templateSearcher.Get().OfType<ManagementClass>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                string templateName = Convert.ToString(templateClass["__CLASS"], CultureInfo.InvariantCulture) ?? "(unnamed event)";
                int version = GetQualifierInt32(templateClass, "eventversion") ?? categoryVersion;
                string opcode = GetQualifierString(templateClass, "eventtypename") ?? string.Empty;
                IReadOnlyList<EtwSchemaParameter> parameters = ReadWmiParameters(templateClass);

                foreach (int eventId in GetQualifierInt32Values(templateClass, "eventtype"))
                {
                    if (eventId < ushort.MinValue || eventId > ushort.MaxValue)
                    {
                        continue;
                    }

                    byte eventVersion = version is >= byte.MinValue and <= byte.MaxValue ? (byte)version : (byte)0;
                    events[$"{category}{eventId,6}{eventVersion,6}{templateName}"] = new EtwSchemaEvent(
                        templateName,
                        (ushort)eventId,
                        eventVersion,
                        string.IsNullOrWhiteSpace(opcode) ? "0" : opcode,
                        "0",
                        parameters);
                }
            }
        }

        if (events.Count == 0)
        {
            return null;
        }

        return new EtwProviderSchema(provider, [.. events.Values], []);
    }

    private static ManagementClass? FindWmiProviderClass(Guid providerId, CancellationToken cancellationToken)
    {
        using var providerSearcher = new ManagementObjectSearcher(
            "root\\WMI",
            "SELECT * FROM meta_class WHERE __superclass = 'EventTrace'");

        foreach (ManagementClass candidateClass in providerSearcher.Get().OfType<ManagementClass>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? guid = GetQualifierString(candidateClass, "guid");
            if (Guid.TryParse(guid, out Guid candidateProviderId) && candidateProviderId == providerId)
            {
                return candidateClass;
            }
        }

        return null;
    }

    private static EtwSchemaParameter[] ReadWmiParameters(ManagementClass templateClass)
    {
        var parameters = new SortedDictionary<int, EtwSchemaParameter>();

        foreach (PropertyData property in templateClass.Properties)
        {
            int? wmiDataId = GetQualifierInt32(property, "wmidataid");
            if (wmiDataId is null)
            {
                continue;
            }

            parameters[wmiDataId.Value] = new EtwSchemaParameter(
                property.Name,
                MapWmiType(property.Type));
        }

        return [.. parameters.Values];
    }

    private static string? GetQualifierString(ManagementClass owner, string qualifierName)
    {
        return owner.Qualifiers
            .OfType<QualifierData>()
            .FirstOrDefault(qualifier => string.Equals(qualifier.Name, qualifierName, StringComparison.OrdinalIgnoreCase))
            ?.Value as string;
    }

    private static int? GetQualifierInt32(ManagementClass owner, string qualifierName)
    {
        object? value = owner.Qualifiers
            .OfType<QualifierData>()
            .FirstOrDefault(qualifier => string.Equals(qualifier.Name, qualifierName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return value is int intValue ? intValue : null;
    }

    private static int? GetQualifierInt32(PropertyData owner, string qualifierName)
    {
        object? value = owner.Qualifiers
            .OfType<QualifierData>()
            .FirstOrDefault(qualifier => string.Equals(qualifier.Name, qualifierName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return value is int intValue ? intValue : null;
    }

    private static IEnumerable<int> GetQualifierInt32Values(ManagementClass owner, string qualifierName)
    {
        object? value = owner.Qualifiers
            .OfType<QualifierData>()
            .FirstOrDefault(qualifier => string.Equals(qualifier.Name, qualifierName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (value is int intValue)
        {
            yield return intValue;
        }
        else if (value is Array values)
        {
            foreach (object item in values)
            {
                if (item is int itemValue)
                {
                    yield return itemValue;
                }
            }
        }
    }

    private static List<TdhNative.EventDescriptor> EnumerateEventDescriptors(
        EtwProviderInfo provider,
        List<string> diagnostics)
    {
        uint bufferSize = 0;
        Guid providerId = provider.Id;
        uint result = TdhNative.TdhEnumerateManifestProviderEvents(ref providerId, IntPtr.Zero, ref bufferSize);
        if (result is TdhNative.ErrorNotFound or TdhNative.ErrorResourceNotPresent)
        {
            return [];
        }

        if (result != TdhNative.ErrorInsufficientBuffer && result != TdhNative.ErrorSuccess)
        {
            diagnostics.Add($"TDH could not enumerate events for this provider. Win32 error: {result}.");
            return [];
        }

        if (bufferSize == 0)
        {
            return [];
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));

        try
        {
            result = TdhNative.TdhEnumerateManifestProviderEvents(ref providerId, buffer, ref bufferSize);
            if (result != TdhNative.ErrorSuccess)
            {
                diagnostics.Add($"TDH could not read event descriptors for this provider. Win32 error: {result}.");
                return [];
            }

            var header = Marshal.PtrToStructure<TdhNative.ProviderEventInfoHeader>(buffer);
            var events = new List<TdhNative.EventDescriptor>(checked((int)header.NumberOfEvents));
            int headerSize = Marshal.SizeOf<TdhNative.ProviderEventInfoHeader>();
            int eventDescriptorSize = Marshal.SizeOf<TdhNative.EventDescriptor>();

            for (int i = 0; i < header.NumberOfEvents; i++)
            {
                IntPtr eventAddress = IntPtr.Add(buffer, headerSize + (i * eventDescriptorSize));
                events.Add(Marshal.PtrToStructure<TdhNative.EventDescriptor>(eventAddress));
            }

            return events;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static EtwSchemaEvent? TryReadEventInformation(
        EtwProviderInfo provider,
        TdhNative.EventDescriptor eventDescriptor,
        List<string> diagnostics)
    {
        uint bufferSize = 0;
        Guid providerId = provider.Id;
        TdhNative.EventDescriptor descriptor = eventDescriptor;
        uint result = TdhNative.TdhGetManifestEventInformation(ref providerId, ref descriptor, IntPtr.Zero, ref bufferSize);
        if (result is TdhNative.ErrorNotFound or TdhNative.ErrorResourceNotPresent)
        {
            diagnostics.Add($"Static metadata is not available for event {eventDescriptor.Id}, version {eventDescriptor.Version}.");
            return null;
        }

        if (result != TdhNative.ErrorInsufficientBuffer && result != TdhNative.ErrorSuccess)
        {
            diagnostics.Add($"TDH could not size metadata for event {eventDescriptor.Id}, version {eventDescriptor.Version}. Win32 error: {result}.");
            return null;
        }

        if (bufferSize == 0)
        {
            return CreateEventWithDescriptorOnly(eventDescriptor);
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));

        try
        {
            result = TdhNative.TdhGetManifestEventInformation(ref providerId, ref descriptor, buffer, ref bufferSize);
            if (result != TdhNative.ErrorSuccess)
            {
                diagnostics.Add($"TDH could not read metadata for event {eventDescriptor.Id}, version {eventDescriptor.Version}. Win32 error: {result}.");
                return CreateEventWithDescriptorOnly(eventDescriptor);
            }

            var header = Marshal.PtrToStructure<TdhNative.TraceEventInfoHeader>(buffer);
            var parameters = ReadParameters(buffer, header);

            return new EtwSchemaEvent(
                GetEventName(buffer, header, eventDescriptor),
                eventDescriptor.Id,
                eventDescriptor.Version,
                GetOffsetString(buffer, header.OpcodeNameOffset, eventDescriptor.Opcode.ToString()),
                GetOffsetString(buffer, header.LevelNameOffset, eventDescriptor.Level.ToString()),
                parameters);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static EtwSchemaEvent CreateEventWithDescriptorOnly(TdhNative.EventDescriptor eventDescriptor)
    {
        return new EtwSchemaEvent(
            $"Event {eventDescriptor.Id}",
            eventDescriptor.Id,
            eventDescriptor.Version,
            eventDescriptor.Opcode.ToString(),
            eventDescriptor.Level.ToString(),
            []);
    }

    private static List<EtwSchemaParameter> ReadParameters(
        IntPtr buffer,
        TdhNative.TraceEventInfoHeader header)
    {
        if (header.TopLevelPropertyCount == 0)
        {
            return [];
        }

        var parameters = new List<EtwSchemaParameter>(checked((int)header.TopLevelPropertyCount));
        int headerSize = Marshal.SizeOf<TdhNative.TraceEventInfoHeader>();
        int propertyInfoSize = Marshal.SizeOf<TdhNative.EventPropertyInfo>();

        for (int i = 0; i < header.TopLevelPropertyCount; i++)
        {
            IntPtr propertyAddress = IntPtr.Add(buffer, headerSize + (i * propertyInfoSize));
            var propertyInfo = Marshal.PtrToStructure<TdhNative.EventPropertyInfo>(propertyAddress);

            parameters.Add(new EtwSchemaParameter(
                GetOffsetString(buffer, propertyInfo.NameOffset, $"Parameter {i + 1}"),
                MapTdhInputType(propertyInfo.InType)));
        }

        return parameters;
    }

    private static string GetEventName(
        IntPtr buffer,
        TdhNative.TraceEventInfoHeader header,
        TdhNative.EventDescriptor eventDescriptor)
    {
        string taskName = GetOffsetString(buffer, header.TaskNameOffset, string.Empty);
        if (!string.IsNullOrWhiteSpace(taskName))
        {
            return taskName;
        }

        string message = GetOffsetString(buffer, header.EventMessageOffset, string.Empty);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return $"Event {eventDescriptor.Id}";
    }

    private static string GetOffsetString(IntPtr buffer, uint offset, string fallback)
    {
        if (offset == 0)
        {
            return fallback;
        }

        IntPtr stringAddress = IntPtr.Add(buffer, checked((int)offset));
        string? value = Marshal.PtrToStringUni(stringAddress);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatSchemaSource(EtwProviderSchemaSource schemaSource)
    {
        return schemaSource switch
        {
            EtwProviderSchemaSource.XmlManifest => "XML manifest",
            EtwProviderSchemaSource.Wbem => "WMI/MOF",
            EtwProviderSchemaSource.Wpp => "WPP",
            EtwProviderSchemaSource.TraceLogging => "TraceLogging",
            _ => "unknown"
        };
    }

    private static string MapTdhInputType(ushort inputType)
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

    private static string MapManifestInputType(string inputType)
    {
        string normalizedType = inputType.StartsWith("win:", StringComparison.OrdinalIgnoreCase)
            ? inputType[4..]
            : inputType;

        return normalizedType switch
        {
            "" => "Unknown",
            "Null" => "Null",
            "UnicodeString" => "WideString",
            "AnsiString" => "AnsiString",
            "Int8" => "Int8",
            "UInt8" => "UInt8",
            "Int16" => "Short",
            "UInt16" => "UShort",
            "Int32" => "Integer",
            "UInt32" => "UInteger",
            "Int64" => "Int64",
            "UInt64" => "UInt64",
            "Float" => "Float",
            "Double" => "Double",
            "Boolean" => "Boolean",
            "Binary" => "Binary",
            "GUID" => "Guid",
            "Guid" => "Guid",
            "Pointer" => "Pointer",
            "FILETIME" => "FileTime",
            "FileTime" => "FileTime",
            "SYSTEMTIME" => "SystemTime",
            "SystemTime" => "SystemTime",
            "SID" => "Sid",
            "Sid" => "Sid",
            "HexInt32" => "HexInt32",
            "HexInt64" => "HexInt64",
            "CountedString" => "CountedString",
            "CountedAnsiString" => "CountedAnsiString",
            "UnicodeChar" => "UnicodeChar",
            "AnsiChar" => "AnsiChar",
            "SizeT" => "SizeT",
            "struct" => "Struct",
            _ => string.IsNullOrWhiteSpace(inputType) ? "Unknown" : normalizedType
        };
    }

    private static string MapWmiType(CimType type)
    {
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
            CimType.String => "WideString",
            CimType.UInt8 => "UInt8",
            CimType.UInt16 => "UShort",
            CimType.UInt32 => "UInteger",
            CimType.UInt64 => "UInt64",
            _ => type.ToString()
        };
    }
}
