using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VoiceMvp.Api.Services;

public sealed class FfmpegAudioPreprocessor(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<FfmpegAudioPreprocessor> logger) : IAudioPreprocessor
{
    private const double DurationToleranceSeconds = 0.35;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["audio/webm"] = ".webm",
            ["audio/ogg"] = ".ogg",
            ["audio/mp4"] = ".m4a",
            ["audio/mpeg"] = ".mp3",
            ["audio/wav"] = ".wav",
            ["audio/x-wav"] = ".wav"
        };

    public async Task<PreparedAudio> PrepareAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        var normalizedContentType = upload.ContentType.Split(';', 2)[0].Trim();
        if (!AllowedTypes.TryGetValue(normalizedContentType, out var extension))
        {
            throw new AudioValidationException("Formato de áudio não suportado.");
        }

        var tempRoot = configuration["Audio:TempPath"];
        if (string.IsNullOrWhiteSpace(tempRoot))
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "voz-mvp");
        }
        else if (!Path.IsPathRooted(tempRoot))
        {
            tempRoot = Path.Combine(environment.ContentRootPath, tempRoot);
        }

        var workingDirectory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var inputPath = Path.Combine(workingDirectory, $"input{extension}");
            var outputPath = Path.Combine(workingDirectory, "normalized.wav");

            await using (var input = File.Create(inputPath))
            {
                await upload.CopyToAsync(input, cancellationToken);
            }

            var durationSeconds = await ReadDurationAsync(inputPath, cancellationToken);
            var maximumSeconds = configuration.GetValue("Audio:MaxDurationSeconds", 15d);
            if (durationSeconds <= 0.05)
            {
                throw new AudioValidationException("O áudio está vazio.");
            }

            if (durationSeconds > maximumSeconds + DurationToleranceSeconds)
            {
                throw new AudioValidationException($"O áudio deve ter no máximo {maximumSeconds:0} segundos.");
            }

            await RunProcessAsync(
                ResolveFfmpegPath(),
                ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath,
                    "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", outputPath],
                cancellationToken);

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length <= 44)
            {
                throw new AudioProcessingException("O FFmpeg não produziu um áudio válido.");
            }

            return new PreparedAudio(
                workingDirectory,
                outputPath,
                checked((int)Math.Round(durationSeconds * 1000)),
                normalizedContentType);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    private async Task<double> ReadDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync(
            ResolveFfprobePath(),
            ["-v", "error", "-show_entries", "format=duration", "-of",
                "default=noprint_wrappers=1:nokey=1", inputPath],
            cancellationToken);

        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            throw new AudioValidationException("Não foi possível identificar a duração do áudio.");
        }

        return duration;
    }

    private string ResolveFfmpegPath() => configuration["FFmpeg:Path"] ?? "ffmpeg";

    private string ResolveFfprobePath()
    {
        var ffmpegPath = ResolveFfmpegPath();
        var directory = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "ffprobe";
        }

        var extension = Path.GetExtension(ffmpegPath);
        return Path.Combine(directory, $"ffprobe{extension}");
    }

    private async Task<string> RunProcessAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may have exited between the check and the kill request.
                }
            });
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0)
            {
                logger.LogWarning("Audio process {Executable} failed: {Error}", executable, error);
                throw new AudioValidationException("O arquivo enviado não contém um áudio válido.");
            }

            return output;
        }
        catch (Win32Exception exception)
        {
            throw new AudioProcessingException(
                "FFmpeg não está instalado ou não foi encontrado no servidor.", exception);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup. The operating system will eventually clear its temp directory.
        }
    }
}
