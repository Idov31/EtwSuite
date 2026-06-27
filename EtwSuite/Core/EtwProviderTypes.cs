namespace EtwSuite.Core;

public enum EtwProviderSchemaSource
{
    XmlManifest = 0,
    Wbem = 1,
    Wpp = 2,
    TraceLogging = 3,
    Unknown = -1
}

public sealed record EtwProviderInfo(
    string Name,
    Guid Id,
    EtwProviderSchemaSource SchemaSource);

public sealed record EtwSchemaParameter(
    string Name,
    string Type);

public sealed record EtwSchemaEvent(
    string Name,
    ushort Id,
    byte Version,
    string Opcode,
    string Level,
    IReadOnlyList<EtwSchemaParameter> Parameters,
    ulong? Keyword = null,
    byte? Channel = null,
    string? SourcePath = null);

public sealed record EtwProviderSchema(
    EtwProviderInfo Provider,
    IReadOnlyList<EtwSchemaEvent> Events,
    IReadOnlyList<string> Diagnostics);
