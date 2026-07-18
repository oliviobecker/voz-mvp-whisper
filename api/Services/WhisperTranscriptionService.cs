using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceMvp.Api.Services;

public sealed class WhisperTranscriptionService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<WhisperTranscriptionService> logger) : ITranscriptionService, IDisposable
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private WhisperFactory? factory;

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        await processingLock.WaitAsync(cancellationToken);
        try
        {
            var whisperFactory = await GetFactoryAsync(cancellationToken);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("pt")
                .Build();

            var segments = new List<string>();
            await using var wavStream = File.OpenRead(wavPath);
            await foreach (var result in processor.ProcessAsync(wavStream).WithCancellation(cancellationToken))
            {
                var text = result.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
            }

            return string.Join(' ', segments).Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not TranscriptionUnavailableException)
        {
            logger.LogError(exception, "Whisper transcription failed.");
            throw new TranscriptionUnavailableException("Não foi possível transcrever o áudio.", exception);
        }
        finally
        {
            processingLock.Release();
        }
    }

    private async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
        if (factory is not null)
        {
            return factory;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (factory is not null)
            {
                return factory;
            }

            var configuredPath = configuration["Whisper:ModelPath"] ?? "whisper/ggml-base.bin";
            var modelPath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

            if (!File.Exists(modelPath))
            {
                logger.LogInformation("Downloading the multilingual Whisper base model to {ModelPath}.", modelPath);
                var partialPath = $"{modelPath}.download";
                try
                {
                    await using var modelStream = await WhisperGgmlDownloader.Default
                        .GetGgmlModelAsync(GgmlType.Base);
                    await using var file = File.Create(partialPath);
                    await modelStream.CopyToAsync(file, cancellationToken);
                    File.Move(partialPath, modelPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }
                }
            }

            factory = WhisperFactory.FromPath(modelPath);
            return factory;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to initialize the Whisper model.");
            throw new TranscriptionUnavailableException(
                "O modelo de transcrição não pôde ser carregado no servidor.", exception);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    public void Dispose()
    {
        factory?.Dispose();
        initializationLock.Dispose();
        processingLock.Dispose();
    }
}
