using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VoiceMvp.Api.Data;
using VoiceMvp.Api.Models;
using VoiceMvp.Api.Services;

namespace VoiceMvp.Api.Endpoints;

public static class TranscriptionEndpoints
{
    public static IEndpointRouteBuilder MapTranscriptionEndpoints(
        this IEndpointRouteBuilder endpoints,
        long maxUploadBytes)
    {
        var group = endpoints.MapGroup("/api/transcriptions");

        group.MapGet("/", async (VoiceDbContext database, CancellationToken cancellationToken) =>
        {
            var items = await database.Transcriptions
                .AsNoTracking()
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .Take(10)
                .Select(item => new TranscriptionResponse(
                    item.Id,
                    item.Text,
                    item.CreatedAtUtc,
                    item.DurationMs,
                    item.ProcessingMs))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        });

        group.MapPost("/", async (
            HttpRequest request,
            IAudioPreprocessor audioPreprocessor,
            ITranscriptionService transcriptionService,
            VoiceDbContext database,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("TranscriptionUpload");

            if (!request.HasFormContentType)
            {
                return Results.Problem(
                    title: "Envio inválido",
                    detail: "Envie o áudio como multipart/form-data.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.ContentLength > maxUploadBytes)
            {
                return Results.Problem(
                    title: "Áudio muito grande",
                    detail: "O arquivo deve ter no máximo 2 MB.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            try
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var audio = form.Files.GetFile("audio");
                if (audio is null || audio.Length == 0)
                {
                    return Results.Problem(
                        title: "Áudio ausente",
                        detail: "Grave um áudio antes de enviar.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (audio.Length > maxUploadBytes)
                {
                    return Results.Problem(
                        title: "Áudio muito grande",
                        detail: "O arquivo deve ter no máximo 2 MB.",
                        statusCode: StatusCodes.Status413PayloadTooLarge);
                }

                using var preparedAudio = await audioPreprocessor.PrepareAsync(audio, cancellationToken);
                var stopwatch = Stopwatch.StartNew();
                var text = await transcriptionService.TranscribeAsync(
                    preparedAudio.WavPath,
                    cancellationToken);
                stopwatch.Stop();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Results.Problem(
                        title: "Nenhuma fala encontrada",
                        detail: "Não conseguimos identificar uma fala clara nesse áudio.",
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                var record = new TranscriptionRecord
                {
                    Text = text,
                    CreatedAtUtc = DateTime.UtcNow,
                    DurationMs = preparedAudio.DurationMs,
                    ProcessingMs = checked((int)stopwatch.ElapsedMilliseconds),
                    AudioFormat = preparedAudio.SourceFormat
                };

                database.Transcriptions.Add(record);
                await database.SaveChangesAsync(cancellationToken);

                var response = new TranscriptionResponse(
                    record.Id,
                    record.Text,
                    record.CreatedAtUtc,
                    record.DurationMs,
                    record.ProcessingMs);

                return Results.Created($"/api/transcriptions/{record.Id}", response);
            }
            catch (AudioValidationException exception)
            {
                return Results.Problem(
                    title: "Áudio inválido",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AudioProcessingException exception)
            {
                logger.LogError(exception, "Audio preparation is unavailable.");
                return Results.Problem(
                    title: "Conversão indisponível",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (TranscriptionUnavailableException exception)
            {
                logger.LogError(exception, "Transcription is unavailable.");
                return Results.Problem(
                    title: "Transcrição indisponível",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidDataException)
            {
                return Results.Problem(
                    title: "Envio inválido",
                    detail: "Não foi possível ler o arquivo enviado.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).DisableAntiforgery();

        return endpoints;
    }
}
