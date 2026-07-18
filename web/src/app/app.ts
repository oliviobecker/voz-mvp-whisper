import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { finalize } from 'rxjs';
import { Transcription, TranscriptionApi } from './transcription-api';

type RecorderState = 'idle' | 'recording' | 'ready' | 'uploading';

@Component({
  selector: 'app-root',
  imports: [DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  private readonly api = inject(TranscriptionApi);
  private readonly maximumDurationMs = 15_000;

  protected readonly state = signal<RecorderState>('idle');
  protected readonly elapsedMs = signal(0);
  protected readonly history = signal<Transcription[]>([]);
  protected readonly latest = signal<Transcription | null>(null);
  protected readonly error = signal('');
  protected readonly loadingHistory = signal(true);
  protected readonly audioUrl = signal<string | null>(null);

  protected readonly timerLabel = computed(() => {
    const seconds = Math.min(15, Math.floor(this.elapsedMs() / 1000));
    return `00:${seconds.toString().padStart(2, '0')}`;
  });
  protected readonly progress = computed(() =>
    Math.min(100, (this.elapsedMs() / this.maximumDurationMs) * 100),
  );

  private recorder?: MediaRecorder;
  private stream?: MediaStream;
  private chunks: Blob[] = [];
  private audioBlob?: Blob;
  private startedAt = 0;
  private timerId?: number;
  private stopId?: number;
  private discardOnStop = false;

  ngOnInit(): void {
    this.loadHistory();
  }

  protected async startRecording(): Promise<void> {
    this.error.set('');
    if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
      this.error.set('Este navegador não oferece suporte à gravação de áudio.');
      return;
    }

    try {
      this.clearPreparedAudio();
      this.stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
        },
      });

      const mimeType = this.pickMimeType();
      this.recorder = mimeType
        ? new MediaRecorder(this.stream, { mimeType, audioBitsPerSecond: 64_000 })
        : new MediaRecorder(this.stream);
      this.chunks = [];
      this.discardOnStop = false;

      this.recorder.addEventListener('dataavailable', (event) => {
        if (event.data.size > 0) this.chunks.push(event.data);
      });
      this.recorder.addEventListener('stop', () => this.finishRecording());
      this.recorder.addEventListener('error', () => {
        this.error.set('A gravação foi interrompida pelo navegador.');
        this.releaseMicrophone();
        this.state.set('idle');
      });

      this.startedAt = performance.now();
      this.elapsedMs.set(0);
      this.state.set('recording');
      this.recorder.start(250);
      this.timerId = window.setInterval(() => {
        this.elapsedMs.set(performance.now() - this.startedAt);
      }, 100);
      this.stopId = window.setTimeout(() => this.stopRecording(), this.maximumDurationMs);
    } catch (cause) {
      this.releaseMicrophone();
      this.state.set('idle');
      this.error.set(this.microphoneError(cause));
    }
  }

  protected stopRecording(): void {
    if (this.state() !== 'recording' || this.recorder?.state === 'inactive') return;
    this.elapsedMs.set(Math.min(this.maximumDurationMs, performance.now() - this.startedAt));
    this.clearTimers();
    this.recorder?.stop();
  }

  protected cancelRecording(): void {
    this.error.set('');
    if (this.state() === 'recording' && this.recorder?.state !== 'inactive') {
      this.discardOnStop = true;
      this.clearTimers();
      this.recorder?.stop();
      return;
    }

    this.clearPreparedAudio();
    this.state.set('idle');
    this.elapsedMs.set(0);
  }

  protected sendRecording(): void {
    if (!this.audioBlob || this.state() !== 'ready') return;

    this.error.set('');
    this.state.set('uploading');
    const filename = `gravacao.${this.extensionFor(this.audioBlob.type)}`;

    this.api
      .create(this.audioBlob, filename)
      .pipe(
        finalize(() => {
          if (this.state() === 'uploading') this.state.set('ready');
        }),
      )
      .subscribe({
        next: (transcription) => {
          this.latest.set(transcription);
          this.history.update((items) => [transcription, ...items].slice(0, 10));
          this.clearPreparedAudio();
          this.elapsedMs.set(0);
          this.state.set('idle');
        },
        error: (cause: HttpErrorResponse) => {
          this.error.set(
            cause.error?.detail ?? 'Não foi possível enviar o áudio. Tente novamente.',
          );
        },
      });
  }

  protected formatSeconds(milliseconds: number): string {
    return `${Math.max(1, Math.round(milliseconds / 1000))}s`;
  }

  protected formatProcessing(milliseconds: number): string {
    return `${(milliseconds / 1000).toFixed(1).replace('.', ',')}s`;
  }

  private finishRecording(): void {
    this.clearTimers();
    this.releaseMicrophone();

    if (this.discardOnStop) {
      this.discardOnStop = false;
      this.chunks = [];
      this.elapsedMs.set(0);
      this.state.set('idle');
      return;
    }

    const mimeType = this.recorder?.mimeType || this.chunks[0]?.type || 'audio/webm';
    this.audioBlob = new Blob(this.chunks, { type: mimeType });
    this.chunks = [];

    if (this.audioBlob.size === 0) {
      this.error.set('Nenhum áudio foi capturado. Tente gravar novamente.');
      this.state.set('idle');
      return;
    }

    this.audioUrl.set(URL.createObjectURL(this.audioBlob));
    this.state.set('ready');
  }

  private loadHistory(): void {
    this.api
      .list()
      .pipe(finalize(() => this.loadingHistory.set(false)))
      .subscribe({
        next: (items) => this.history.set(items),
        error: () => this.error.set('Não foi possível carregar o histórico.'),
      });
  }

  private pickMimeType(): string | undefined {
    return [
      'audio/webm;codecs=opus',
      'audio/mp4',
      'audio/ogg;codecs=opus',
      'audio/webm',
    ].find((type) => MediaRecorder.isTypeSupported(type));
  }

  private extensionFor(mimeType: string): string {
    if (mimeType.includes('mp4')) return 'm4a';
    if (mimeType.includes('ogg')) return 'ogg';
    if (mimeType.includes('wav')) return 'wav';
    return 'webm';
  }

  private microphoneError(cause: unknown): string {
    if (cause instanceof DOMException && cause.name === 'NotAllowedError') {
      return 'Permita o acesso ao microfone para começar a gravar.';
    }
    if (cause instanceof DOMException && cause.name === 'NotFoundError') {
      return 'Nenhum microfone foi encontrado neste aparelho.';
    }
    return 'Não foi possível acessar o microfone.';
  }

  private clearPreparedAudio(): void {
    const url = this.audioUrl();
    if (url) URL.revokeObjectURL(url);
    this.audioUrl.set(null);
    this.audioBlob = undefined;
  }

  private clearTimers(): void {
    if (this.timerId) window.clearInterval(this.timerId);
    if (this.stopId) window.clearTimeout(this.stopId);
    this.timerId = undefined;
    this.stopId = undefined;
  }

  private releaseMicrophone(): void {
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = undefined;
  }

  ngOnDestroy(): void {
    this.clearTimers();
    this.releaseMicrophone();
    this.clearPreparedAudio();
  }
}
