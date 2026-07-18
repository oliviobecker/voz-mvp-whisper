namespace VoiceMvp.Api.Services;

public interface IAudioPreprocessor
{
    Task<PreparedAudio> PrepareAsync(IFormFile upload, CancellationToken cancellationToken);
}

public sealed class PreparedAudio(string workingDirectory, string wavPath, int durationMs, string sourceFormat)
    : IDisposable
{
    public string WavPath { get; } = wavPath;
    public int DurationMs { get; } = durationMs;
    public string SourceFormat { get; } = sourceFormat;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
        catch
        {
            // Temporary audio cleanup is best effort and must not change a successful response.
        }
    }
}

public sealed class AudioValidationException(string message) : Exception(message);

public sealed class AudioProcessingException(string message, Exception? innerException = null)
    : Exception(message, innerException);
