namespace VoiceMvp.Api.Models;

public sealed class TranscriptionRecord
{
    public long Id { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int DurationMs { get; set; }
    public int ProcessingMs { get; set; }
    public required string AudioFormat { get; set; }
}

public sealed record TranscriptionResponse(
    long Id,
    string Text,
    DateTime CreatedAtUtc,
    int DurationMs,
    int ProcessingMs);
