# WinUI Guidance

## Presentation Only

WinUI pages should be thin. Keep ETW session logic, provider decoding, XML
parsing, business rules, and filtering/query execution out of code-behind.

Preferred folders:

```text
Views/
ViewModels/
Controls/
Converters/
Services/
Resources/
```

Code-behind is acceptable for control-specific behavior, navigation glue,
UI-only interactions, and view initialization.

## MVVM

Use observable view models for UI state and async commands for work that can
block. Suggested responsibilities:

- `ProvidersViewModel`: load providers, search/filter, open details.
- `ProviderDetailsViewModel`: display keywords, levels, tasks, opcodes, event
  descriptors, and templates.
- `SessionsViewModel`: create, start, stop, and monitor trace sessions.
- `LiveEventsViewModel`: display streamed events, pause/resume, filter, clear,
  and export.
- `ManifestViewModel`: load manifests, display parsed schema, and show
  diagnostics.

Do not block the UI thread:

```csharp
Providers = await providerCatalog.EnumerateProvidersAsync(cancellationToken);
```

Avoid:

```csharp
var providers = providerCatalog.EnumerateProvidersAsync(CancellationToken.None).Result;
```

## UX Priorities

This is a technical tool for advanced Windows users, developers, performance
engineers, and security researchers. Prioritize accuracy, inspectability,
searchability, explicit configuration, clear failures, copy/export support, and
reproducibility.

Good UI behavior:

- Show provider name and GUID.
- Show raw and decoded event IDs.
- Show keyword masks in hex and decoded names.
- Show levels, tasks, and opcodes with raw numeric values.
- Show whether metadata is manifest, MOF, TraceLogging, unknown, or partial.
- Allow copying provider GUIDs, event IDs, session configs, and payload fields.

Design for thousands of providers, hundreds of thousands of events, high-rate
streams, large manifests, and large ETL files. Use virtualization-friendly
controls and batched UI updates.

