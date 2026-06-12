# ETW Backend Guidance

## TraceEvent First

Use `Microsoft.Diagnostics.Tracing.TraceEvent` where practical for:

- Creating and controlling trace sessions.
- Enabling providers.
- Consuming live events.
- Reading ETL files.
- Dynamic event parsing.

Use TDH or ADVAPI32 APIs when TraceEvent does not expose needed provider
metadata or native behavior.

## Sessions

ETW sessions are system resources. Session-owning types must implement
`IDisposable` or `IAsyncDisposable` and clean up on cancellation, crash paths,
and UI close.

Preferred shape:

```csharp
public interface IEtwTraceSession : IAsyncDisposable
{
    string Name { get; }

    Task EnableProviderAsync(
        Guid providerId,
        EtwProviderEnableOptions options,
        CancellationToken cancellationToken);

    Task DisableProviderAsync(
        Guid providerId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<EtwEventRecord> ReadEventsAsync(
        CancellationToken cancellationToken);
}
```

Handle access denied, session already exists, session not found, invalid keyword
masks, and provider-not-found cases explicitly.

## Metadata

Use TDH for metadata gaps:

- Enumerating registered providers.
- Querying provider fields.
- Resolving event metadata.
- Inspecting keywords, levels, tasks, opcodes, and templates.
- Decoding manifest/MOF metadata when needed.

Metadata may be missing, partial, malformed, unavailable, version-dependent, or
access-restricted. Support manifest-based, MOF/classic, TraceLogging, unknown,
and partially decoded providers.

Preserve raw values where practical:

- Provider GUID.
- Event ID and version.
- Raw task, opcode, level, and keyword values.
- Decoded display names when available.
- Raw and decoded payload values when available.

## Native Interop

Isolate native calls in dedicated classes, for example:

```text
Native/
  TdhNative.cs
  Advapi32Native.cs
  Win32Error.cs
```

Do not expose native structs outside `EtwSuite.Etw`. Prefer safe wrappers around
raw P/Invoke. Document non-obvious API behavior, especially two-call buffer-size
patterns and `ERROR_INSUFFICIENT_BUFFER` handling.

