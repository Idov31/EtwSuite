using EtwSuite.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class EtwFilterCompilerTests
{
    private static readonly Guid ProviderId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public void BasicProviderFilter_MatchesContainsWildcardAndGuid()
    {
        var provider = new EtwProviderInfo(
            "Microsoft-Windows-Kernel-Process",
            ProviderId,
            EtwProviderSchemaSource.TraceLogging);

        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, "kernel").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, "Microsoft-*Process").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, "????osoft-*").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, ProviderId.ToString("D")).Matches(provider));
        Assert.IsFalse(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, "Microsoft-*Registry").Matches(provider));
    }

    [TestMethod]
    public void BasicEventFilter_MatchesCoreFieldsAndPayload()
    {
        EtwLiveEventRecord record = CreateEventRecord();

        Assert.IsTrue(EtwFilterCompiler.CompileEventFilter(EtwFilterMode.Basic, "process start").Matches(record));
        Assert.IsTrue(EtwFilterCompiler.CompileEventFilter(EtwFilterMode.Basic, "powershell").Matches(record));
        Assert.IsTrue(EtwFilterCompiler.CompileEventFilter(EtwFilterMode.Basic, "Image*").Matches(record));
        Assert.IsTrue(EtwFilterCompiler.CompileEventFilter(EtwFilterMode.Basic, "*cmd.exe").Matches(record));
        Assert.IsTrue(EtwFilterCompiler.CompileEventFilter(EtwFilterMode.Basic, "4242").Matches(record));
    }

    [TestMethod]
    public void SqlProviderFilter_MatchesSupportedFields()
    {
        var provider = new EtwProviderInfo(
            "Microsoft-Windows-Kernel-Process",
            ProviderId,
            EtwProviderSchemaSource.TraceLogging);

        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, "name LIKE 'Microsoft-*'").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, $"id = '{ProviderId:D}'").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, "schema_source = 'TraceLogging'").Matches(provider));
        Assert.IsTrue(EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, "WHERE guid = '11111111-2222-3333-4444-555555555555'").Matches(provider));
    }

    [TestMethod]
    public void SqlProviderFilter_ReturnsStructuredErrors()
    {
        EtwCompiledFilter<EtwProviderInfo> unknownField =
            EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, "missing = 1");
        EtwCompiledFilter<EtwProviderInfo> malformed =
            EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.SQL, "name = ");

        Assert.IsFalse(unknownField.IsValid);
        Assert.IsTrue(unknownField.ErrorMessage?.Contains("Unknown field", StringComparison.Ordinal) == true);
        Assert.IsFalse(malformed.IsValid);
        Assert.IsFalse(malformed.Matches(new EtwProviderInfo("name", Guid.Empty, EtwProviderSchemaSource.Unknown)));
    }

    [TestMethod]
    public void SqlEventFilter_MatchesNumericStringPayloadAndPrecedence()
    {
        EtwLiveEventRecord record = CreateEventRecord();

        Assert.IsTrue(EtwFilterCompiler
            .CompileEventFilter(EtwFilterMode.SQL, "event_id = 1 AND process_name LIKE 'powershell%'")
            .Matches(record));
        Assert.IsTrue(EtwFilterCompiler
            .CompileEventFilter(EtwFilterMode.SQL, "level <= 4")
            .Matches(record));
        Assert.IsTrue(EtwFilterCompiler
            .CompileEventFilter(EtwFilterMode.SQL, "payload.ImageName LIKE '*cmd.exe'")
            .Matches(record));
        Assert.IsTrue(EtwFilterCompiler
            .CompileEventFilter(EtwFilterMode.SQL, "event_id = 2 OR (event_id = 1 AND process_name = 'powershell.exe')")
            .Matches(record));
        Assert.IsFalse(EtwFilterCompiler
            .CompileEventFilter(EtwFilterMode.SQL, "(event_id = 2 OR event_id = 3) AND process_name = 'powershell.exe'")
            .Matches(record));
    }

    private static EtwLiveEventRecord CreateEventRecord()
    {
        return new EtwLiveEventRecord(
            DateTimeOffset.Parse("2026-06-12T10:15:30Z"),
            "Microsoft-Windows-Kernel-Process",
            ProviderId,
            "Process Start",
            1,
            0,
            0,
            4,
            4242,
            "powershell.exe",
            123,
            new[]
            {
                new EtwPayloadValue("ImageName", "UnicodeString", @"C:\Windows\System32\cmd.exe"),
                new EtwPayloadValue("CommandLine", "UnicodeString", "cmd.exe /c whoami"),
            });
    }
}
