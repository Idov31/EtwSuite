# Manifest and XML Guidance

## Parsing

For small manifests, `XDocument` is acceptable. For large or untrusted XML,
prefer `XmlReader`.

Required XML safety settings:

```csharp
var settings = new XmlReaderSettings
{
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null
};
```

Do not allow external entity resolution. Treat manifests as untrusted input and
avoid unsafe deserialization.

## Models

Parse into project-owned models. Do not pass `XElement`, `XmlNode`, or parser
types into the UI.

Example shape:

```csharp
public sealed record EtwManifest(
    IReadOnlyList<EtwManifestProvider> Providers);

public sealed record EtwManifestProvider(
    Guid Id,
    string Name,
    IReadOnlyList<EtwManifestEvent> Events,
    IReadOnlyList<EtwManifestKeyword> Keywords,
    IReadOnlyList<EtwManifestTask> Tasks,
    IReadOnlyList<EtwManifestOpcode> Opcodes,
    IReadOnlyList<EtwManifestTemplate> Templates);
```

## Diagnostics

Manifest parsing should collect diagnostics for normal validation problems:

```csharp
public sealed record ManifestDiagnostic(
    ManifestDiagnosticSeverity Severity,
    string Message,
    int? Line,
    int? Column);
```

Use exceptions for programmer errors and unexpected failures, not normal
manifest validation issues.

