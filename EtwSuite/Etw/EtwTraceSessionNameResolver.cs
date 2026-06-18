using EtwSuite.Core;

namespace EtwSuite.Etw;

internal static class EtwTraceSessionNameResolver
{
    private static readonly Guid SecurityAuditingProviderId = Guid.Parse("54849625-5478-4994-a5ba-3e3b0328c30d");

    private static readonly EtwTraceSessionDescriptor SecurityEventLogSession =
        new("EventLog-Security", CanStopSession: false);

    private static readonly Dictionary<Guid, EtwTraceSessionDescriptor> SpecialSessionNamesByProviderId =
        new Dictionary<Guid, EtwTraceSessionDescriptor>
        {
            [SecurityAuditingProviderId] = SecurityEventLogSession
        };

    private static readonly Dictionary<string, EtwTraceSessionDescriptor> SpecialSessionNamesByProviderName =
        new Dictionary<string, EtwTraceSessionDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft-Windows-Security-Auditing"] = SecurityEventLogSession,
            ["EventLog-Application"] = new("EventLog-Application", CanStopSession: true),
            ["EventLog-Security"] = SecurityEventLogSession,
            ["EventLog-Setup"] = new("EventLog-Setup", CanStopSession: true),
            ["EventLog-System"] = new("EventLog-System", CanStopSession: true)
        };

    public static EtwTraceSessionDescriptor ResolveSession(
        EtwProviderEnableOptions options,
        string generatedPrefix,
        int providerNameLength)
    {
        if (SpecialSessionNamesByProviderId.TryGetValue(options.ProviderId, out var session))
        {
            return session;
        }

        if (SpecialSessionNamesByProviderName.TryGetValue(options.ProviderName, out session))
        {
            return session;
        }

        return new EtwTraceSessionDescriptor(
            CreateGeneratedSessionName(options.ProviderName, generatedPrefix, providerNameLength),
            CanStopSession: true);
    }

    private static string CreateGeneratedSessionName(
        string providerName,
        string prefix,
        int providerNameLength)
    {
        string sanitizedProviderName = new([.. providerName
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(providerNameLength)]);

        if (string.IsNullOrWhiteSpace(sanitizedProviderName))
        {
            sanitizedProviderName = "Provider";
        }

        return $"{prefix}{sanitizedProviderName}-{Guid.NewGuid():N}"[..64];
    }
}

internal sealed record EtwTraceSessionDescriptor(
    string SessionName,
    bool CanStopSession);
