using System.Runtime.InteropServices;

namespace EtwSuite.Etw.Native;

internal static class TdhNative
{
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorInsufficientBuffer = 122;
    internal const uint ErrorNotFound = 1168;
    internal const uint ErrorResourceNotPresent = 4316;

    [DllImport("tdh.dll", ExactSpelling = true)]
    internal static extern uint TdhEnumerateProviders(
        IntPtr providerEnumerationInfo,
        ref uint bufferSize);

    [DllImport("tdh.dll", ExactSpelling = true)]
    internal static extern uint TdhEnumerateManifestProviderEvents(
        ref Guid providerGuid,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("tdh.dll", ExactSpelling = true)]
    internal static extern uint TdhGetManifestEventInformation(
        ref Guid providerGuid,
        ref EventDescriptor eventDescriptor,
        IntPtr buffer,
        ref uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ProviderEnumerationInfoHeader
    {
        public readonly uint NumberOfProviders;
        public readonly uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct TraceProviderInfo
    {
        public readonly Guid ProviderGuid;
        public readonly uint SchemaSource;
        public readonly uint ProviderNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ProviderEventInfoHeader
    {
        public readonly uint NumberOfEvents;
        public readonly uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventDescriptor
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct TraceEventInfoHeader
    {
        public readonly Guid ProviderGuid;
        public readonly Guid EventGuid;
        public readonly EventDescriptor EventDescriptor;
        public readonly int DecodingSource;
        public readonly uint ProviderNameOffset;
        public readonly uint LevelNameOffset;
        public readonly uint ChannelNameOffset;
        public readonly uint KeywordsNameOffset;
        public readonly uint TaskNameOffset;
        public readonly uint OpcodeNameOffset;
        public readonly uint EventMessageOffset;
        public readonly uint ProviderMessageOffset;
        public readonly uint BinaryXmlOffset;
        public readonly uint BinaryXmlSize;
        public readonly uint ActivityIdNameOffset;
        public readonly uint RelatedActivityIdNameOffset;
        public readonly uint PropertyCount;
        public readonly uint TopLevelPropertyCount;
        public readonly uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct EventPropertyInfo
    {
        public readonly uint Flags;
        public readonly uint NameOffset;
        public readonly ushort InType;
        public readonly ushort OutType;
        public readonly uint MapNameOffset;
        public readonly ushort Count;
        public readonly ushort Length;
        public readonly uint Reserved;
    }
}
