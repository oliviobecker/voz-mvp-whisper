namespace VoiceMvp.Api.Services;

public interface ITranscriptionService
{
    Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken);
}

public sealed class TranscriptionUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
