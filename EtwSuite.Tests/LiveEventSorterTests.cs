using EtwSuite.Core;
using EtwSuite.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class LiveEventSorterTests
{
    private static readonly Guid ProviderId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public void Sort_TimeAscending_OrdersByConsumedAt()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("B", consumedAt: "2026-06-12T10:15:32Z"),
            CreateRecord("A", consumedAt: "2026-06-12T10:15:30Z"),
            CreateRecord("C", consumedAt: "2026-06-12T10:15:34Z")
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records,
            LiveEventSortField.Time,
            LiveEventSortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, sorted.Select(record => record.EventName).ToArray());
    }

    [TestMethod]
    public void Sort_TimeDescending_OrdersByConsumedAt()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("B", consumedAt: "2026-06-12T10:15:32Z"),
            CreateRecord("A", consumedAt: "2026-06-12T10:15:30Z"),
            CreateRecord("C", consumedAt: "2026-06-12T10:15:34Z")
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records,
            LiveEventSortField.Time,
            LiveEventSortDirection.Descending);

        CollectionAssert.AreEqual(new[] { "C", "B", "A" }, sorted.Select(record => record.EventName).ToArray());
    }

    [TestMethod]
    public void Sort_NumericFields_UsesNumericValues()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("Ten", eventId: 10),
            CreateRecord("Two", eventId: 2),
            CreateRecord("One", eventId: 1)
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records,
            LiveEventSortField.Id,
            LiveEventSortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { "One", "Two", "Ten" }, sorted.Select(record => record.EventName).ToArray());
    }

    [TestMethod]
    public void Sort_StringFields_IsCaseInsensitive()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("Third", processName: "zsh.exe"),
            CreateRecord("First", processName: "Alpha.exe"),
            CreateRecord("Second", processName: "beta.exe")
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records,
            LiveEventSortField.ProcessName,
            LiveEventSortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { "First", "Second", "Third" }, sorted.Select(record => record.EventName).ToArray());
    }

    [TestMethod]
    public void Sort_Parameters_UsesPayloadCount()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("Two", payloadCount: 2),
            CreateRecord("Zero", payloadCount: 0),
            CreateRecord("One", payloadCount: 1)
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records,
            LiveEventSortField.Parameters,
            LiveEventSortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { "Zero", "One", "Two" }, sorted.Select(record => record.EventName).ToArray());
    }

    [TestMethod]
    public void Sort_EqualKeys_PreservesFilteredSourceOrder()
    {
        EtwLiveEventRecord[] records =
        [
            CreateRecord("Ignored", level: 1, processId: 10),
            CreateRecord("First", level: 4, processId: 42),
            CreateRecord("Second", level: 4, processId: 42),
            CreateRecord("Third", level: 4, processId: 42)
        ];

        IReadOnlyList<EtwLiveEventRecord> sorted = LiveEventSorter.Sort(
            records.Where(record => record.Level == 4),
            LiveEventSortField.ProcessId,
            LiveEventSortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { "First", "Second", "Third" }, sorted.Select(record => record.EventName).ToArray());
    }

    private static EtwLiveEventRecord CreateRecord(
        string eventName,
        string consumedAt = "2026-06-12T10:15:30Z",
        ushort eventId = 1,
        byte version = 0,
        byte opcode = 0,
        byte level = 4,
        uint processId = 4242,
        string processName = "process.exe",
        uint threadId = 123,
        int payloadCount = 1)
    {
        return new EtwLiveEventRecord(
            DateTimeOffset.Parse(consumedAt),
            "Provider",
            ProviderId,
            eventName,
            eventId,
            version,
            opcode,
            level,
            processId,
            processName,
            threadId,
            Enumerable.Range(0, payloadCount)
                .Select(index => new EtwPayloadValue($"Field{index}", "String", index.ToString()))
                .ToArray());
    }
}
