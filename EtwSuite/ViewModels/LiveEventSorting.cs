using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public enum LiveEventSortField
{
    Time,
    Provider,
    Event,
    Id,
    Version,
    Opcode,
    Level,
    ProcessId,
    ProcessName,
    ThreadId,
    Parameters
}

public enum LiveEventSortDirection
{
    Ascending,
    Descending
}

internal static class LiveEventSorter
{
    public static IReadOnlyList<EtwLiveEventRecord> Sort(
        IEnumerable<EtwLiveEventRecord> records,
        LiveEventSortField sortField,
        LiveEventSortDirection sortDirection)
    {
        return sortDirection == LiveEventSortDirection.Ascending
            ? SortAscending(records, sortField).ToArray()
            : SortDescending(records, sortField).ToArray();
    }

    private static IOrderedEnumerable<EtwLiveEventRecord> SortAscending(
        IEnumerable<EtwLiveEventRecord> records,
        LiveEventSortField sortField)
    {
        return sortField switch
        {
            LiveEventSortField.Time => records.OrderBy(record => record.ConsumedAt),
            LiveEventSortField.Provider => records.OrderBy(record => record.ProviderName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.Event => records.OrderBy(record => record.EventName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.Id => records.OrderBy(record => record.EventId),
            LiveEventSortField.Version => records.OrderBy(record => record.Version),
            LiveEventSortField.Opcode => records.OrderBy(record => record.Opcode),
            LiveEventSortField.Level => records.OrderBy(record => record.Level),
            LiveEventSortField.ProcessId => records.OrderBy(record => record.ProcessId),
            LiveEventSortField.ProcessName => records.OrderBy(record => record.ProcessName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.ThreadId => records.OrderBy(record => record.ThreadId),
            LiveEventSortField.Parameters => records.OrderBy(record => record.Payload.Count),
            _ => records.OrderBy(record => record.ConsumedAt)
        };
    }

    private static IOrderedEnumerable<EtwLiveEventRecord> SortDescending(
        IEnumerable<EtwLiveEventRecord> records,
        LiveEventSortField sortField)
    {
        return sortField switch
        {
            LiveEventSortField.Time => records.OrderByDescending(record => record.ConsumedAt),
            LiveEventSortField.Provider => records.OrderByDescending(record => record.ProviderName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.Event => records.OrderByDescending(record => record.EventName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.Id => records.OrderByDescending(record => record.EventId),
            LiveEventSortField.Version => records.OrderByDescending(record => record.Version),
            LiveEventSortField.Opcode => records.OrderByDescending(record => record.Opcode),
            LiveEventSortField.Level => records.OrderByDescending(record => record.Level),
            LiveEventSortField.ProcessId => records.OrderByDescending(record => record.ProcessId),
            LiveEventSortField.ProcessName => records.OrderByDescending(record => record.ProcessName, StringComparer.OrdinalIgnoreCase),
            LiveEventSortField.ThreadId => records.OrderByDescending(record => record.ThreadId),
            LiveEventSortField.Parameters => records.OrderByDescending(record => record.Payload.Count),
            _ => records.OrderByDescending(record => record.ConsumedAt)
        };
    }
}
