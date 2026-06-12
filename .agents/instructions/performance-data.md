# Performance and Data Guidance

ETW workloads can be high-volume. Prefer streaming and bounded memory usage.

Use:

- Batching.
- `IAsyncEnumerable<T>`.
- `System.Threading.Channels`.
- Backpressure-aware channels.
- Immutable or append-only event batches.
- Virtualized UI lists/grids.
- Lazy metadata loading.
- Provider metadata caching.

Avoid:

- Loading everything eagerly.
- Repeated TDH queries for the same provider.
- Per-event UI collection updates during high-rate traces.
- Blocking the UI thread.
- Unbounded live event buffers.
- Converting every payload value to string too early.

Suggested bounded channel pattern:

```csharp
Channel<EtwEventRecord> eventChannel = Channel.CreateBounded<EtwEventRecord>(
    new BoundedChannelOptions(capacity: 100_000)
    {
        SingleWriter = false,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
```

Dropped events must use an explicit policy and be surfaced in the UI.

Filtering must be UI-independent. Start with a simple testable model:

```csharp
public sealed record EtwEventFilter(
    Guid? ProviderId,
    string? ProviderNameContains,
    int? EventId,
    int? ProcessId,
    int? ThreadId,
    string? PayloadField,
    string? PayloadContains,
    DateTimeOffset? From,
    DateTimeOffset? To);
```

Possible future query syntax:

```text
provider == "Microsoft-Windows-Kernel-Process" and event_id == 1
pid == 1234
payload.ImageName contains "powershell"
level <= Warning
```

Keep early implementations simple and testable.

