using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using VoiceMvp.Api.Data;
using VoiceMvp.Api.Endpoints;
using VoiceMvp.Api.Services;

const long maxUploadBytes = 2 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

var connectionString = builder.Configuration.GetConnectionString("VoiceDatabase")
    ?? "Data Source=storage/voice-mvp.db";
var connectionBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
var dataSource = connectionBuilder.DataSource;
if (!string.IsNullOrWhiteSpace(dataSource))
{
    var absoluteDataSource = Path.IsPathRooted(dataSource)
        ? dataSource
        : Path.Combine(builder.Environment.ContentRootPath, dataSource);
    Directory.CreateDirectory(Path.GetDirectoryName(absoluteDataSource)!);
    connectionBuilder.DataSource = absoluteDataSource;
    connectionString = connectionBuilder.ToString();
}

builder.Services.AddDbContext<VoiceDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<IAudioPreprocessor, FfmpegAudioPreprocessor>();
builder.Services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("development", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors("development");
}

app.UseDefaultFiles();
app.UseStaticFiles();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<VoiceDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapTranscriptionEndpoints(maxUploadBytes);

var indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
if (File.Exists(indexPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
