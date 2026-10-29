import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { appConfig } from './app.config';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [...appConfig.providers, provideHttpClientTesting()],
    }).compileComponents();
  });

  it('uygulama oluşturulur', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('başlığı ve İstanbul saatine çevrilmiş health zamanını gösterir', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/health')
      .flush({ durum: 'ok', zaman: '2026-10-29T11:20:00Z' });
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('KlinikAkış');
    // 11:20 UTC → 14:20 İstanbul, tarayıcının dilimi ne olursa olsun
    expect(compiled.textContent).toContain('29.10.2026 14:20');
  });
});
