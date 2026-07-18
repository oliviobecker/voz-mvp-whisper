using Microsoft.EntityFrameworkCore;
using VoiceMvp.Api.Models;

namespace VoiceMvp.Api.Data;

public sealed class VoiceDbContext(DbContextOptions<VoiceDbContext> options) : DbContext(options)
{
    public DbSet<TranscriptionRecord> Transcriptions => Set<TranscriptionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transcription = modelBuilder.Entity<TranscriptionRecord>();
        transcription.ToTable("Transcriptions");
        transcription.HasKey(item => item.Id);
        transcription.Property(item => item.Text).IsRequired();
        transcription.Property(item => item.AudioFormat).HasMaxLength(80).IsRequired();
        transcription.HasIndex(item => item.CreatedAtUtc);
    }
}
