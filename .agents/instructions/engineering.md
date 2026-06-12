# Engineering Guidance

## Coding Style

- Prefer clear code over clever code.
- Prefer small focused types.
- Prefer immutable records for domain data.
- Prefer interfaces at architectural boundaries.
- Use nullable reference types.
- Avoid global mutable state and service locator patterns.
- Use dependency injection where it simplifies testing.
- Keep public APIs stable and documented.
- Do not swallow exceptions silently.
- Do not use `async void` except for UI event handlers.
- Avoid `.Result` and `.Wait()` on tasks.

## Naming

Use precise ETW terminology:

```text
EtwProviderInfo
EtwProviderIdentity
EtwProviderMetadata
EtwEventRecord
EtwTraceSession
EtwSessionOptions
EtwProviderEnableOptions
EtwManifest
EtwManifestProvider
EtwManifestEvent
EtwEventFilter
EtwDecodeResult
```

Avoid vague names such as `DataItem`, `EventData`, `InfoObject`, `Helper`,
`Utils`, and `Processor`. Use `Manager` only for types that genuinely own
lifecycle and coordination.

## Dependencies

Allowed dependencies should serve a clear purpose. Acceptable examples:

- `Microsoft.Diagnostics.Tracing.TraceEvent`
- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.DependencyInjection`
- `CommunityToolkit.Mvvm`
- `System.CommandLine`
- `Microsoft.Data.Sqlite`
- `Microsoft.Windows.CsWin32`
- `Vanara.PInvoke`, if used consistently

Avoid adding dependencies for trivial functionality or replacing the UI stack.

## Testing

Core logic should be testable without WinUI. Test:

- Manifest parsing.
- Provider model normalization.
- Event filter logic.
- Query parsing.
- Export serialization.
- TDH buffer handling where practical.
- Session option validation.
- Error mapping from Win32/HRESULT values.

Use sample manifests and small ETL files as test fixtures where possible. Tests
that require live ETW sessions or administrator privileges should be marked as
integration tests.

## CLI

A CLI is strongly recommended even if the main product is a WinUI app. It
should exercise the same backend as the UI.

Useful commands:

```text
etwsuite providers list
etwsuite providers show <name-or-guid>
etwsuite manifest parse <path>
etwsuite session start <config>
etwsuite session stop <name>
etwsuite trace open <etl-path>
etwsuite trace export <etl-path> --format json
```

## Documentation

When adding user-visible features, document supported provider types, metadata
limitations, privilege requirements, export formats, session behavior, lossy
decoding behavior, and known ETW API quirks.

