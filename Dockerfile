FROM node:24-alpine AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY api/VoiceMvp.Api.csproj api/
RUN dotnet restore api/VoiceMvp.Api.csproj
COPY api/ api/
RUN dotnet publish api/VoiceMvp.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=api-build /app/publish ./
COPY --from=web-build /src/web/dist/voz-web/browser ./wwwroot

RUN mkdir -p /app/storage /app/models \
    && chown -R app:app /app

USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__VoiceDatabase="Data Source=/app/storage/voice-mvp.db" \
    Whisper__ModelPath=/app/models/ggml-base.bin \
    FFmpeg__Path=ffmpeg
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "VoiceMvp.Api.dll"]
