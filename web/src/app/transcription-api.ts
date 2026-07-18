import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface Transcription {
  id: number;
  text: string;
  createdAtUtc: string;
  durationMs: number;
  processingMs: number;
}

@Injectable({ providedIn: 'root' })
export class TranscriptionApi {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/transcriptions';

  list(): Observable<Transcription[]> {
    return this.http.get<Transcription[]>(this.endpoint);
  }

  create(audio: Blob, filename: string): Observable<Transcription> {
    const form = new FormData();
    form.append('audio', audio, filename);
    return this.http.post<Transcription>(this.endpoint, form);
  }
}
