namespace EtwSuite.Core;

public enum EtwTraceSessionState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public sealed record EtwProviderEnableOptions(
    string ProviderName,
    Guid ProviderId,
    byte Level = 5,
    ulong AnyKeyword = ulong.MaxValue,
    ulong AllKeyword = 0);

public sealed record EtwPayloadValue(
    string Name,
    string Type,
    string Value);

public sealed record EtwLiveEventRecord(
    DateTimeOffset ConsumedAt,
    string ProviderName,
    Guid ProviderId,
    string EventName,
    ushort EventId,
    byte Version,
    byte Opcode,
    byte Level,
    uint ProcessId,
    string ProcessName,
    uint ThreadId,
    IReadOnlyList<EtwPayloadValue> Payload);

