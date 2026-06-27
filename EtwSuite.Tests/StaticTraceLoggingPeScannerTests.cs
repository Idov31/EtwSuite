using System.Text;
using EtwSuite.Core;
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
        Assert.AreEqual(TraceLoggingEventOwnershipConfidence.SingleProvider, result.Providers[0].Events[0].OwnershipConfidence);
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

    [TestMethod]
    public void ParseTraceLoggingMetadataForTests_DoesNotDuplicateEventsForMultiProviderBinariesWithoutOwnershipRefs()
    {
        using var stream = new MemoryStream();
        WriteEtw0Header(stream);
        WriteProviderBlob(
            stream,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Synthetic.Provider.One",
            null);
        WriteProviderBlob(
            stream,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Synthetic.Provider.Two",
            null);
        WriteEventBlob(stream);
        stream.WriteByte(1);

        var result = StaticTraceLoggingPeScanner.ParseTraceLoggingMetadataForTests(stream.ToArray());

        Assert.AreEqual(2, result.Providers.Count);
        Assert.IsTrue(result.Providers.All(provider => provider.Events.Count == 0));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("could not be correlated", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ParseTraceLoggingSectionsForTests_UsesDirectPrecedingProviderReference()
    {
        CreateMultiProviderMetadata(
            out byte[] metadata,
            out ulong firstRegistrationAddress,
            out _,
            out ulong eventAddress);
        ulong dataAddress = 0x20000000;
        byte[] data = CreateProviderData(dataAddress, firstRegistrationAddress);
        byte[] code = CreateCodeReferences(dataAddress, eventAddress);

        var result = StaticTraceLoggingPeScanner.ParseTraceLoggingSectionsForTests(
            metadata,
            0x10000000,
            data,
            dataAddress,
            code,
            0x30000000);

        Assert.AreEqual(1, result.Providers[0].Events.Count);
        Assert.AreEqual(0, result.Providers[1].Events.Count);
        Assert.AreEqual(TraceLoggingEventOwnershipConfidence.DirectPreceding, result.Providers[0].Events[0].OwnershipConfidence);
    }

    [TestMethod]
    public void ParseTraceLoggingSectionsForTests_UsesDirectNearestProviderReference()
    {
        CreateMultiProviderMetadata(
            out byte[] metadata,
            out ulong firstRegistrationAddress,
            out _,
            out ulong eventAddress);
        ulong dataAddress = 0x20000000;
        byte[] data = CreateProviderData(dataAddress, firstRegistrationAddress);
        byte[] code = CreateCodeReferences(eventAddress, dataAddress);

        var result = StaticTraceLoggingPeScanner.ParseTraceLoggingSectionsForTests(
            metadata,
            0x10000000,
            data,
            dataAddress,
            code,
            0x30000000);

        Assert.AreEqual(1, result.Providers[0].Events.Count);
        Assert.AreEqual(TraceLoggingEventOwnershipConfidence.DirectNearest, result.Providers[0].Events[0].OwnershipConfidence);
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

    private static void CreateMultiProviderMetadata(
        out byte[] metadata,
        out ulong firstRegistrationAddress,
        out ulong secondRegistrationAddress,
        out ulong eventAddress)
    {
        const ulong metadataAddress = 0x10000000;
        using var stream = new MemoryStream();
        WriteEtw0Header(stream);
        long firstRegistrationOffset = WriteProviderBlob(
            stream,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Synthetic.Provider.One",
            null);
        long secondRegistrationOffset = WriteProviderBlob(
            stream,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Synthetic.Provider.Two",
            null);
        long eventOffset = WriteEventBlob(stream);
        stream.WriteByte(1);

        metadata = stream.ToArray();
        firstRegistrationAddress = metadataAddress + (ulong)firstRegistrationOffset;
        secondRegistrationAddress = metadataAddress + (ulong)secondRegistrationOffset;
        eventAddress = metadataAddress + (ulong)eventOffset;
    }

    private static byte[] CreateProviderData(ulong providerBaseAddress, ulong registrationAddress)
    {
        byte[] data = new byte[32];
        BitConverter.GetBytes(registrationAddress).CopyTo(data, 8);
        return data;
    }

    private static byte[] CreateCodeReferences(params ulong[] targets)
    {
        using var stream = new MemoryStream();
        for (int index = 0; index < targets.Length; index++)
        {
            stream.WriteByte(0x48);
            stream.WriteByte((byte)(0xB8 + index));
            stream.Write(BitConverter.GetBytes(targets[index]));
        }

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

    private static long WriteProviderBlob(Stream stream, Guid providerId, Guid? groupId)
    {
        return WriteProviderBlob(stream, providerId, "Synthetic.Provider", groupId);
    }

    private static long WriteProviderBlob(Stream stream, Guid providerId, string nameText, Guid? groupId)
    {
        stream.WriteByte(4);
        stream.Write(providerId.ToByteArray());
        long registrationOffset = stream.Position;
        byte[] name = Encoding.UTF8.GetBytes(nameText);
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

        return registrationOffset;
    }

    private static long WriteEventBlob(Stream stream)
    {
        long eventOffset = stream.Position;
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
        return eventOffset;
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
