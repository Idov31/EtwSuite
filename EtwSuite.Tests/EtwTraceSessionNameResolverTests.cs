using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class EtwTraceSessionNameResolverTests
{
    private static readonly Guid SecurityAuditingProviderId =
        Guid.Parse("54849625-5478-4994-a5ba-3e3b0328c30d");

    [TestMethod]
    public void ResolveSession_UsesNonStoppableSecurityEventLogSessionForSecurityAuditingProviderName()
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions("Microsoft-Windows-Security-Auditing", Guid.NewGuid()),
            "EtwSuite-",
            48);

        Assert.AreEqual("EventLog-Security", session.SessionName);
        Assert.IsFalse(session.CanStopSession);
    }

    [TestMethod]
    public void ResolveSession_UsesNonStoppableSecurityEventLogSessionForSecurityAuditingProviderId()
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions("Security Auditing", SecurityAuditingProviderId),
            "EtwSuite-",
            48);

        Assert.AreEqual("EventLog-Security", session.SessionName);
        Assert.IsFalse(session.CanStopSession);
    }

    [DataTestMethod]
    [DataRow("EventLog-Application")]
    [DataRow("EventLog-Security")]
    [DataRow("EventLog-Setup")]
    [DataRow("EventLog-System")]
    public void ResolveSession_PreservesSpecialEventLogSessionNames(string providerName)
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions(providerName, Guid.NewGuid()),
            "EtwSuite-",
            48);

        Assert.AreEqual(providerName, session.SessionName);
    }

    [DataTestMethod]
    [DataRow("EventLog-Application")]
    [DataRow("EventLog-Setup")]
    [DataRow("EventLog-System")]
    public void ResolveSession_AllowsStoppingStoppableEventLogSessionNames(string providerName)
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions(providerName, Guid.NewGuid()),
            "EtwSuite-",
            48);

        Assert.IsTrue(session.CanStopSession);
    }

    [TestMethod]
    public void ResolveSession_TreatsEventLogSecurityAsNonStoppable()
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions("EventLog-Security", Guid.NewGuid()),
            "EtwSuite-",
            48);

        Assert.AreEqual("EventLog-Security", session.SessionName);
        Assert.IsFalse(session.CanStopSession);
    }

    [TestMethod]
    public void ResolveSession_GeneratesStoppableEtwSuiteSessionNameForStandardProvider()
    {
        EtwTraceSessionDescriptor session = EtwTraceSessionNameResolver.ResolveSession(
            new EtwProviderEnableOptions("Microsoft-Windows-Kernel-Process", Guid.NewGuid()),
            "EtwSuite-",
            48);

        StringAssert.StartsWith(session.SessionName, "EtwSuite-Microsoft-Windows-Kernel-Process-");
        Assert.IsTrue(session.SessionName.Length <= 64);
        Assert.IsTrue(session.CanStopSession);
    }

    [TestMethod]
    public async Task StartAsync_RejectsEtlRecordingForNonStoppableSpecialSession()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.etl");
        var recorder = new TraceEventEtlRecorder();

        NotSupportedException exception = await Assert.ThrowsExceptionAsync<NotSupportedException>(async () =>
        {
            await recorder.StartAsync(
                new EtwProviderEnableOptions("Microsoft-Windows-Security-Auditing", SecurityAuditingProviderId),
                filePath,
                CancellationToken.None);
        });

        Assert.AreEqual(
            "ETL recording is not supported for EventLog-Security because the session is owned by Windows and cannot be stopped or reconfigured.",
            exception.Message);
    }
}
