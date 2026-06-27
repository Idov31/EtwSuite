using System.Text;
using EtwSuite.Etw.TraceLogging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class StaticTraceLoggingPeScannerTests
{
    [TestMethod]
    public void ParseTraceLoggingMetadataForTests_ParsesProviderAndEventBlobs()
    {
        Guid providerId = Guid.Parse("11111111-2222-3333-4455-66778899aabb");
        Guid groupId = Guid.Parse("aaaaaaaa-bbbb-cccc-ddee-ff0011223344");

        byte[] metadata = CreateTraceLoggingMetadata(providerId, groupId);

        var result = StaticTraceLoggingPeScanner.ParseTraceLoggingMetadataForTests(metadata);

        Assert.AreEqual(1, result.Providers.Count);
        Assert.AreEqual("Synthetic.Provider", result.Providers[0].Name);
        Assert.AreEqual(providerId, result.Providers[0].Id);
        Assert.AreEqual(groupId, result.Providers[0].GroupId);
        Assert.AreEqual(1, result.Providers[0].Events.Count);
        Assert.AreEqual("SyntheticEvent", result.Providers[0].Events[0].Name);
        Assert.AreEqual((byte)11, result.Providers[0].Events[0].Channel);
        Assert.AreEqual((byte)5, result.Providers[0].Events[0].Level);
        Assert.AreEqual((byte)2, result.Providers[0].Events[0].Opcode);
        Assert.AreEqual(0x4000UL, result.Providers[0].Events[0].Keyword);
        Assert.AreEqual("ProcessId", result.Providers[0].Events[0].Fields[0].Name);
        Assert.AreEqual("UInt32", result.Providers[0].Events[0].Fields[0].Type);
    }

    [TestMethod]
    public void ParseTraceLoggingMetadataForTests_ReportsMalformedEventBlob()
    {
        byte[] metadata = CreateMalformedTraceLoggingMetadata();

        var result = StaticTraceLoggingPeScanner.ParseTraceLoggingMetadataForTests(metadata);

        Assert.AreEqual(1, result.Providers.Count);
        Assert.AreEqual(1, result.Diagnostics.Count);
        StringAssert.Contains(result.Diagnostics[0].Message, "Malformed TraceLogging event blob");
    }

    private static byte[] CreateTraceLoggingMetadata(Guid providerId, Guid groupId)
    {
        using var stream = new MemoryStream();
        WriteEtw0Header(stream);
        WriteProviderBlob(stream, providerId, groupId);
        WriteEventBlob(stream);
        stream.WriteByte(1);
        return stream.ToArray();
    }

    private static byte[] CreateMalformedTraceLoggingMetadata()
    {
        using var stream = new MemoryStream();
        WriteEtw0Header(stream);
        WriteProviderBlob(stream, Guid.Parse("11111111-1111-1111-1111-111111111111"), null);
        stream.WriteByte(3);
        stream.WriteByte(11);
        stream.WriteByte(5);
        stream.WriteByte(0);
        WriteUInt64(stream, 0);
        WriteUInt16(stream, 1000);
        return stream.ToArray();
    }

    private static void WriteEtw0Header(Stream stream)
    {
        stream.Write("ETW0"u8);
        WriteUInt16(stream, 16);
        stream.WriteByte(0);
        stream.WriteByte(1);
        WriteUInt64(stream, 0xBB8A052B88040E86UL);
    }

    private static void WriteProviderBlob(Stream stream, Guid providerId, Guid? groupId)
    {
        stream.WriteByte(4);
        stream.Write(providerId.ToByteArray());
        byte[] name = Encoding.UTF8.GetBytes("Synthetic.Provider");
        ushort traitsLength = groupId is null ? (ushort)0 : (ushort)19;
        WriteUInt16(stream, checked((ushort)(2 + name.Length + 1 + traitsLength)));
        stream.Write(name);
        stream.WriteByte(0);
        if (groupId is not null)
        {
            WriteUInt16(stream, 19);
            stream.WriteByte(1);
            stream.Write(groupId.Value.ToByteArray());
        }
    }

    private static void WriteEventBlob(Stream stream)
    {
        stream.WriteByte(3);
        stream.WriteByte(11);
        stream.WriteByte(5);
        stream.WriteByte(2);
        WriteUInt64(stream, 0x4000);

        using var payload = new MemoryStream();
        payload.WriteByte(0);
        payload.Write(Encoding.UTF8.GetBytes("SyntheticEvent"));
        payload.WriteByte(0);
        payload.Write(Encoding.UTF8.GetBytes("ProcessId"));
        payload.WriteByte(0);
        payload.WriteByte(8);

        byte[] payloadBytes = payload.ToArray();
        WriteUInt16(stream, checked((ushort)(2 + payloadBytes.Length)));
        stream.Write(payloadBytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }
}
