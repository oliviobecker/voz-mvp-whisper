import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { App } from './app';
import { TranscriptionApi } from './transcription-api';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        {
          provide: TranscriptionApi,
          useValue: {
            list: () => of([]),
            create: () => of(),
          },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the recording experience', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const content = fixture.nativeElement.textContent as string;
    expect(content).toContain('Fale. Envie.');
    expect(content).toContain('Gravar áudio');
    expect(content).toContain('Nenhuma transcrição ainda');
  });
});
