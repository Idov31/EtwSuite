using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class TraceEventRecordingReaderTests
{
    [TestMethod]
    public void DetectFormat_ReturnsSupportedFormats()
    {
        var reader = new TraceEventRecordingReader();

        Assert.AreEqual(EtwRecordingFormat.Etl, reader.DetectFormat("trace.etl"));
        Assert.AreEqual(EtwRecordingFormat.Json, reader.DetectFormat("events.JSON"));
        Assert.AreEqual(EtwRecordingFormat.Csv, reader.DetectFormat("events.csv"));
        Assert.AreEqual(EtwRecordingFormat.Unsupported, reader.DetectFormat("events.evtx"));
        Assert.AreEqual(EtwRecordingFormat.Unsupported, reader.DetectFormat("events.txt"));
    }

    [TestMethod]
    public async Task ReadEventsAsync_ReadsEtwSuiteJsonExport()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            filePath,
            """
            [
              {
                "Time": "12:34:56.789",
                "Provider": "Provider-A",
                "Event": "Event One",
                "Id": 7,
                "Version": 1,
                "Opcode": 2,
                "Level": 4,
                "ProcessId": 1234,
                "ProcessName": "proc",
                "ThreadId": 5678,
                "Parameters": [
                  { "Name": "ImageName", "Type": "String", "Value": "cmd.exe" }
                ]
              }
            ]
            """);

        try
        {
            IReadOnlyList<EtwLiveEventRecord> records = await ReadAllAsync(filePath);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("Provider-A", records[0].ProviderName);
            Assert.AreEqual("Event One", records[0].EventName);
            Assert.AreEqual((ushort)7, records[0].EventId);
            Assert.AreEqual((uint)1234, records[0].ProcessId);
            Assert.AreEqual("ImageName", records[0].Payload[0].Name);
            Assert.AreEqual("cmd.exe", records[0].Payload[0].Value);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task ReadEventsAsync_ReadsEtwSuiteCsvExport()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            
            Time,Provider,Event,Id,Version,Opcode,Level,ProcessId,ProcessName,ThreadId,Parameters
            12:34:56.789,Provider-A,Event One,7,1,2,4,1234,proc,5678,"ImageName=cmd.exe; CommandLine=""cmd.exe /c whoami
            
            """);

        try
        {
            IReadOnlyList<EtwLiveEventRecord> records = await ReadAllAsync(filePath);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("Provider-A", records[0].ProviderName);
            Assert.AreEqual("Event One", records[0].EventName);
            Assert.AreEqual((ushort)7, records[0].EventId);
            Assert.AreEqual((uint)5678, records[0].ThreadId);
            Assert.AreEqual("Parameters", records[0].Payload[0].Name);
            Assert.IsTrue(records[0].Payload[0].Value.Contains("cmd.exe", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task ReadEventsAsync_ReturnsStructuredErrorForUnsupportedFiles()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.evtx");
        await File.WriteAllTextAsync(filePath, "not an ETL recording");

        try
        {
            var reader = new TraceEventRecordingReader();
            EtwRecordingException exception = await Assert.ThrowsExceptionAsync<EtwRecordingException>(async () =>
            {
                await foreach (IReadOnlyList<EtwLiveEventRecord> _ in reader.ReadEventsAsync(filePath, 10, CancellationToken.None))
                {
                }
            });

            StringAssert.Contains(exception.Message, "Supported: .etl, .json, .csv");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static async Task<IReadOnlyList<EtwLiveEventRecord>> ReadAllAsync(string filePath)
    {
        var reader = new TraceEventRecordingReader();
        var records = new List<EtwLiveEventRecord>();
        await foreach (IReadOnlyList<EtwLiveEventRecord> batch in reader.ReadEventsAsync(filePath, 10, CancellationToken.None))
        {
            records.AddRange(batch);
        }

        return records;
    }
}
