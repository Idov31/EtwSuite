# AGENTS.md

## Purpose

EtwSuite is a Windows-native ETW inspection suite built with C#, WinUI 3,
Windows App SDK, and .NET. It should become useful both as a GUI tool and as a
reusable ETW backend library.

Core goals:

- List and resolve ETW providers by name and GUID.
- Read provider metadata and parse ETW XML manifests.
- Create, control, and clean up ETW trace sessions.
- Enable providers with explicit levels and keyword masks.
- Consume live events, open ETL files, decode events, filter/search data, and
  export decoded records.

Do not convert this project to MAUI, Electron, Avalonia, or WPF. Do not add
telemetry, cloud upload, or remote diagnostics without explicit approval.

## Always Apply

- Keep UI, core models, and ETW implementation separate.
- Keep WinUI code-behind thin; use view models and service abstractions.
- Put ETW behavior in an ETW/backend layer, not in pages or controls.
- Use project-owned domain models instead of binding UI to `TraceEvent`, XML
  nodes, native structs, pointers, or raw Win32 buffers.
- Use `CancellationToken` for blocking, parsing, enumerating, tracing, export,
  search, and file operations.
- Avoid `.Result`, `.Wait()`, and UI-thread blocking.
- Treat ETW sessions as system resources and dispose/stop them deterministically.
- Treat ETW metadata as incomplete, malformed, version-dependent, or
  access-restricted.
- Return structured errors for expected ETW, native, XML, and permission
  failures. Do not silently swallow them.
- Treat event payloads as sensitive. Do not transmit or redact data unless the
  user explicitly asks.
- Add or update tests for core parsing, filtering, serialization, validation,
  and error mapping when behavior changes.

## Load Topic Guidance

Read only the files relevant to the current task:

- Architecture or project layout changes:
  `.agents/instructions/architecture.md`
- WinUI, MVVM, views, view models, or UX:
  `.agents/instructions/winui.md`
- ETW sessions, TraceEvent, provider enumeration, TDH, ETL decoding, or native
  interop:
  `.agents/instructions/etw-backend.md`
- Manifest or XML parsing:
  `.agents/instructions/manifest-xml.md`
- Filtering, querying, export, high-volume data flow, or buffering:
  `.agents/instructions/performance-data.md`
- Security, privacy, permissions, elevation, packaging, or logging:
  `.agents/instructions/security-privacy.md`
- Tests, CLI work, documentation, dependencies, or naming:
  `.agents/instructions/engineering.md`
- Common local commands:
  `.agents/commands.md`
- Product priorities and MVP sequencing:
  `.agents/roadmap.md`

## Preferred Shape

Target structure as the codebase grows:

```text
src/
  EtwSuite.App.WinUI/   WinUI 3 frontend.
  EtwSuite.Core/        UI-independent domain models and interfaces.
  EtwSuite.Etw/         TraceEvent, TDH, ADVAPI32, ETL, and decoding.
  EtwSuite.Cli/         Optional backend-focused CLI.
tests/
  EtwSuite.Core.Tests/
  EtwSuite.Etw.Tests/
  EtwSuite.App.Tests/
```

The current solution may not yet match this layout. Prefer small incremental
moves toward it instead of broad rewrites.

## Feature Pattern

For a new ETW feature:

```text
Core:
  Define interfaces and domain models.
ETW:
  Implement interfaces with TraceEvent, TDH, or isolated native APIs.
App.WinUI:
  Add view model state and commands, then bind the view.
Tests:
  Cover parsing, filtering, validation, normalization, and error mapping.
Docs:
  Update relevant limitations, privilege notes, formats, or API quirks.
```

Example: provider keywords

```text
EtwSuite.Core:
  EtwProviderKeyword, IEtwProviderMetadataService
EtwSuite.Etw:
  TdhProviderMetadataService
EtwSuite.App.WinUI:
  ProviderDetailsViewModel, ProviderDetailsPage
EtwSuite.Etw.Tests:
  Keyword parsing and native error handling tests
```

## Avoid

- ETW logic in WinUI code-behind.
- UI binding directly to native structs, raw pointers, `TraceEvent`, or XML
  parser types.
- Unbounded live event buffers.
- Updating observable UI collections once per event during high-volume traces.
- Unsafe XML settings, external entity resolution, or unsafe deserialization.
- Unsafe code outside isolated native interop.
- Assuming provider metadata is complete.
- Requiring administrator privileges for basic provider browsing, manifest
  parsing, or cached metadata viewing.
- Hard-coded provider lists except as test fixtures.
- Dependencies that do not serve a clear project need.
