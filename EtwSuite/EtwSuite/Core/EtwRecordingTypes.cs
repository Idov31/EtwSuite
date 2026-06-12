namespace EtwSuite.Core;

public enum EtwRecordingFormat
{
    Etl,
    Json,
    Csv,
    Unsupported
}

public sealed class EtwRecordingException : Exception
{
    public EtwRecordingException(string message)
        : base(message)
    {
    }

    public EtwRecordingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IEtwRecordingReader
{
    EtwRecordingFormat DetectFormat(string filePath);

    IAsyncEnumerable<IReadOnlyList<EtwLiveEventRecord>> ReadEventsAsync(
        string filePath,
        int batchSize,
        CancellationToken cancellationToken);
}
