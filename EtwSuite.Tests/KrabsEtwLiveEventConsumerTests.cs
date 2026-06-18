using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class KrabsEtwLiveEventConsumerTests
{
    [TestMethod]
    public void DecodeReverseCountedWideString_ReadsBigEndianCountAtStart()
    {
        byte[] bytes =
        [
            0x00, 0x04,
            0x48, 0x00,
            0x69, 0x00
        ];

        Assert.AreEqual("Hi", KrabsEtwLiveEventConsumer.DecodeReverseCountedWideString(bytes));
    }

    [TestMethod]
    public void DecodeReverseCountedWideString_ReadsShiftedBigEndianCount()
    {
        byte[] bytes =
        [
            0xFF,
            0x00, 0x04,
            0x48, 0x00,
            0x69, 0x00
        ];

        Assert.AreEqual("Hi", KrabsEtwLiveEventConsumer.DecodeReverseCountedWideString(bytes));
    }

    [TestMethod]
    public void DecodeReverseCountedWideString_ClampsCountToAvailableData()
    {
        byte[] bytes =
        [
            0x00, 0x0A,
            0x41, 0x00,
            0x42, 0x00
        ];

        Assert.AreEqual("AB", KrabsEtwLiveEventConsumer.DecodeReverseCountedWideString(bytes));
    }

    [TestMethod]
    public void DecodeReverseCountedWideString_RoundsOddByteCountDown()
    {
        byte[] bytes =
        [
            0x00, 0x03,
            0x41, 0x00,
            0x7F
        ];

        Assert.AreEqual("A", KrabsEtwLiveEventConsumer.DecodeReverseCountedWideString(bytes));
    }

    [TestMethod]
    public void DecodeReverseCountedWideString_TrimsTrailingNullCharacters()
    {
        byte[] bytes =
        [
            0x00, 0x04,
            0x41, 0x00,
            0x00, 0x00
        ];

        Assert.AreEqual("A", KrabsEtwLiveEventConsumer.DecodeReverseCountedWideString(bytes));
    }
}
