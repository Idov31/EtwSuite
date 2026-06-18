using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Management;

namespace EtwSuite.Tests;

[TestClass]
public sealed class TdhInputTypeMapperTests
{
    [TestMethod]
    public void Map_DistinguishesWideAndReverseCountedWideStrings()
    {
        Assert.AreEqual("WideString", TdhInputTypeMapper.Map(1));
        Assert.AreEqual("ReverseCountedWideString", TdhInputTypeMapper.Map(302));
    }

    [TestMethod]
    public void Map_UsesManifestAndWbemCountedStringRanges()
    {
        Assert.AreEqual("ManifestCountedWideString", TdhInputTypeMapper.Map(22));
        Assert.AreEqual("ManifestCountedAnsiString", TdhInputTypeMapper.Map(23));
        Assert.AreEqual("Reserved", TdhInputTypeMapper.Map(24));
        Assert.AreEqual("ManifestCountedBinary", TdhInputTypeMapper.Map(25));

        Assert.AreEqual("CountedWideString", TdhInputTypeMapper.Map(300));
        Assert.AreEqual("CountedAnsiString", TdhInputTypeMapper.Map(301));
        Assert.AreEqual("ReverseCountedAnsiString", TdhInputTypeMapper.Map(303));
    }

    [TestMethod]
    public void MapWmi_UsesReverseCountedWideStringUnlessExplicitlyNullTerminated()
    {
        Assert.AreEqual("ReverseCountedWideString", TdhInputTypeMapper.MapWmi(CimType.String, null));
        Assert.AreEqual("ReverseCountedWideString", TdhInputTypeMapper.MapWmi(CimType.String, "NotCounted"));
        Assert.AreEqual("WideString", TdhInputTypeMapper.MapWmi(CimType.String, "NullTerminated"));
    }

    [TestMethod]
    public void MapWmi_UsesClassicEtwExtensionQualifiers()
    {
        Assert.AreEqual("ReverseCountedWideString", TdhInputTypeMapper.MapWmi(CimType.Object, null, "RWString"));
        Assert.AreEqual("ReverseCountedAnsiString", TdhInputTypeMapper.MapWmi(CimType.Object, null, "RString"));
        Assert.AreEqual("WbemSid", TdhInputTypeMapper.MapWmi(CimType.Object, null, "Sid"));
    }
}
