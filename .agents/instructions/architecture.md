# Architecture Guidance

## Layers

Prefer a layered Windows-native desktop architecture:

- `EtwSuite.Core`: domain models, filters, session descriptions, interfaces,
  and UI-independent logic.
- `EtwSuite.Etw`: TraceEvent integration, TDH/native interop, provider
  enumeration, event decoding, ETL reading, and safe wrappers.
- `EtwSuite.App.WinUI`: WinUI views, view models, controls, converters,
  navigation, dialogs, app startup, and UI services.
- `EtwSuite.Cli`: optional command-line frontend that exercises the same core
  backend as the UI.

The UI project must not own core ETW business logic.

## Boundaries

Use interfaces at architectural boundaries. A view model may call an interface
such as:

```csharp
public interface IEtwProviderCatalog
{
    Task<IReadOnlyList<EtwProviderInfo>> EnumerateProvidersAsync(
        CancellationToken cancellationToken);
}
```

Convert external data into stable project-owned models:

```csharp
public sealed record EtwProviderInfo(
    Guid Id,
    string Name,
    string? Source,
    string? MessageFilePath,
    string? ResourceFilePath,
    string? ParameterFilePath);
```

```csharp
public sealed record EtwEventRecord(
    DateTimeOffset Timestamp,
    Guid ProviderId,
    string ProviderName,
    int EventId,
    byte Version,
    string? TaskName,
    string? OpcodeName,
    string? LevelName,
    ulong Keywords,
    int ProcessId,
    int ThreadId,
    IReadOnlyDictionary<string, object?> Payload);
```

Provider identity should support both GUID and optional name:

```csharp
public sealed record EtwProviderIdentity(Guid Id, string? Name);
```

## Error Model

Use structured errors for expected failures:

```csharp
public enum EtwErrorKind
{
    AccessDenied,
    ProviderNotFound,
    SessionAlreadyExists,
    SessionNotFound,
    InvalidManifest,
    DecodeFailure,
    NativeApiFailure,
    Unknown
}
```

```csharp
public sealed record EtwOperationError(
    EtwErrorKind Kind,
    string Message,
    int? NativeErrorCode,
    Exception? Exception);
```

Do not show raw HRESULTs or Win32 codes alone. Include them as details with a
clear explanation.

