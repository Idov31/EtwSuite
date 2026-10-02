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

    [DataTestMethod]
    [DataRow(null, "WideString", "AnsiString")]
    [DataRow("", "WideString", "AnsiString")]
    [DataRow("   ", "WideString", "AnsiString")]
    [DataRow("NullTerminated", "WideString", "AnsiString")]
    [DataRow("Counted", "CountedWideString", "CountedAnsiString")]
    [DataRow("ReverseCounted", "ReverseCountedWideString", "ReverseCountedAnsiString")]
    [DataRow("NotCounted", "NonNullTerminatedWideString", "NonNullTerminatedAnsiString")]
    [DataRow(" nUlLtErMiNaTeD ", "WideString", "AnsiString")]
    [DataRow(" cOuNtEd ", "CountedWideString", "CountedAnsiString")]
    [DataRow(" rEvErSeCoUnTeD ", "ReverseCountedWideString", "ReverseCountedAnsiString")]
    [DataRow(" nOtCoUnTeD ", "NonNullTerminatedWideString", "NonNullTerminatedAnsiString")]
    [DataRow(" Unsupported ", "Unknown (StringTermination: Unsupported)", "Unknown (StringTermination: Unsupported)")]
    public void MapWmi_UsesStringWidthAndTermination(string? termination, string wideType, string ansiType)
    {
        Assert.AreEqual(wideType, TdhInputTypeMapper.MapWmi(CimType.String, termination, format: "w"));
        Assert.AreEqual(wideType, TdhInputTypeMapper.MapWmi(CimType.String, termination, format: " W "));
        Assert.AreEqual(ansiType, TdhInputTypeMapper.MapWmi(CimType.String, termination));
        Assert.AreEqual(ansiType, TdhInputTypeMapper.MapWmi(CimType.String, termination, format: " "));
        Assert.AreEqual(ansiType, TdhInputTypeMapper.MapWmi(CimType.String, termination, format: "s"));
    }

    [DataTestMethod]
    [DataRow("RWString", "WideString")]
    [DataRow(" rwSTRING ", "WideString")]
    [DataRow("RString", "AnsiString")]
    [DataRow(" rSTRING ", "AnsiString")]
    [DataRow("Sid", "WbemSid")]
    [DataRow(" sID ", "WbemSid")]
    public void MapWmi_ExtensionsTakePrecedence(string extension, string expectedType)
    {
        Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(CimType.Object, null, extension));
        Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(CimType.String, "Counted", extension));
        Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(CimType.String, "ReverseCounted", extension, "w"));
        Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(CimType.String, "Unsupported", extension, "w"));
    }

    [DataTestMethod]
    [DataRow("CustomExtension")]
    [DataRow("")]
    [DataRow(" ")]
    public void MapWmi_UnrecognizedOrBlankExtensionPreservesStringQualifiers(string extension)
    {
        Assert.AreEqual("CountedWideString", TdhInputTypeMapper.MapWmi(CimType.String, "Counted", extension, "w"));
        Assert.AreEqual("CountedAnsiString", TdhInputTypeMapper.MapWmi(CimType.String, "Counted", extension));
    }

    [TestMethod]
    public void MapWmi_PreservesUnknownObjectExtension()
    {
        Assert.AreEqual(" CustomExtension ", TdhInputTypeMapper.MapWmi(CimType.Object, "Counted", " CustomExtension ", "w"));
    }

    [DataTestMethod]
    [DataRow(CimType.Boolean, "Boolean")]
    [DataRow(CimType.Char16, "UnicodeChar")]
    [DataRow(CimType.DateTime, "SystemTime")]
    [DataRow(CimType.Object, "Struct")]
    [DataRow(CimType.Real32, "Float")]
    [DataRow(CimType.Real64, "Double")]
    [DataRow(CimType.Reference, "Pointer")]
    [DataRow(CimType.SInt8, "Int8")]
    [DataRow(CimType.SInt16, "Short")]
    [DataRow(CimType.SInt32, "Integer")]
    [DataRow(CimType.SInt64, "Int64")]
    [DataRow(CimType.UInt8, "UInt8")]
    [DataRow(CimType.UInt16, "UShort")]
    [DataRow(CimType.UInt32, "UInteger")]
    [DataRow(CimType.UInt64, "UInt64")]
    public void MapWmi_PreservesNonStringTypes(CimType type, string expectedType)
    {
        Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(type, "Counted", format: "w"));
        if (type != CimType.Object)
        {
            Assert.AreEqual(expectedType, TdhInputTypeMapper.MapWmi(type, "Counted", "CustomExtension", "w"));
        }
    }
}
