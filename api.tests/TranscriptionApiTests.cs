using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoiceMvp.Api.Data;
using VoiceMvp.Api.Models;
using VoiceMvp.Api.Services;

namespace VoiceMvp.Api.Tests;

public sealed class TranscriptionApiTests
{
    [Fact]
    public async Task Upload_creates_a_transcription_and_adds_it_to_history()
    {
        await using var factory = new VoiceApiFactory();
        using var client = factory.CreateClient();
        using var content = CreateAudioUpload();

        var response = await client.PostAsync("/api/transcriptions", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TranscriptionResponse>();
        Assert.NotNull(created);
        Assert.Equal("Buscar uma caixa em Blumenau.", created.Text);
        Assert.Equal(1_250, created.DurationMs);

        var history = await client.GetFromJsonAsync<List<TranscriptionResponse>>("/api/transcriptions");
        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal(created.Id, history[0].Id);
    }

    [Fact]
    public async Task Upload_without_audio_returns_a_clear_client_error()
    {
        await using var factory = new VoiceApiFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync("/api/transcriptions", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_is_limited_to_ten_newest_records()
    {
        await using var factory = new VoiceApiFactory();
        using var client = factory.CreateClient();

        for (var index = 0; index < 12; index++)
        {
            using var content = CreateAudioUpload();
            var response = await client.PostAsync("/api/transcriptions", content);
            response.EnsureSuccessStatusCode();
        }

        var history = await client.GetFromJsonAsync<List<TranscriptionResponse>>("/api/transcriptions");

        Assert.Equal(10, history!.Count);
        Assert.True(history[0].Id > history[^1].Id);
    }

    private static MultipartFormDataContent CreateAudioUpload()
    {
        var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent([1, 2, 3, 4]);
        audio.Headers.ContentType = new("audio/webm");
        content.Add(audio, "audio", "recording.webm");
        return content;
    }
}

internal sealed class VoiceApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"voice-mvp-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VoiceDbContext>>();
            services.RemoveAll<VoiceDbContext>();
            services.RemoveAll<IAudioPreprocessor>();
            services.RemoveAll<ITranscriptionService>();
            services.AddDbContext<VoiceDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            services.AddSingleton<IAudioPreprocessor, FakeAudioPreprocessor>();
            services.AddSingleton<ITranscriptionService, FakeTranscriptionService>();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }
}

internal sealed class FakeAudioPreprocessor : IAudioPreprocessor
{
    public Task<PreparedAudio> PrepareAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"voice-mvp-fake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "normalized.wav");
        File.WriteAllBytes(path, [0]);
        return Task.FromResult(new PreparedAudio(directory, path, 1_250, upload.ContentType));
    }
}

internal sealed class FakeTranscriptionService : ITranscriptionService
{
    public Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken) =>
        Task.FromResult("Buscar uma caixa em Blumenau.");
}
