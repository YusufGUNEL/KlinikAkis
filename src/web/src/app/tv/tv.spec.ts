import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { TvCagrisi } from '../cekirdek/api';
import { Tv } from './tv';

const BRANSLAR = [{ id: 'endo', kod: 'Endokrinoloji', ad: 'Endokrinoloji', hekimSayisi: 1 }];
const cagri = (siraNo: string, oda: string, basHarfler: string, zaman: string): TvCagrisi => ({ siraNo, oda, basHarfler, zaman });

function kur() {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Tv);
  fixture.componentRef.setInput('bransId', 'endo');
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

describe('Tv', () => {
  it('bölüm adını Türkçe büyük harfle gösterir, çağrı yoksa boş durum', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').flush(BRANSLAR);
    http.expectOne('/api/tv/cagrilar').flush([]);
    await fixture.whenStable();
    expect(el.querySelector('h1')!.textContent).toContain('ENDOKRİNOLOJİ POLİKLİNİĞİ');
    expect(el.textContent).toContain('Henüz çağrı yok');
  });

  it('son çağrı büyük, öncekiler "Son çağrılar" satırında; yalnız baş harf', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').flush(BRANSLAR);
    http.expectOne('/api/tv/cagrilar').flush([
      cagri('A-017', '3', 'A.K.', '2026-10-31T11:21:00Z'),
      cagri('A-016', '3', 'M.Y.', '2026-10-31T11:10:00Z'),
      cagri('A-015', '2', 'Z.A.', '2026-10-31T11:02:00Z'),
    ]);
    await fixture.whenStable();
    const son = el.querySelector('[data-test="son-cagri"]')!.textContent!.replace(/\s+/g, ' ');
    expect(son).toContain('A-017 →, ODA 3');
    expect(son).toContain('(A.K.)');
    const onceki = el.querySelector('[data-test="onceki-cagrilar"]')!.textContent!.replace(/\s+/g, ' ');
    expect(onceki).toContain('Son çağrılar:');
    expect(onceki).toContain('A-016 →, oda 3');
    expect(onceki).toContain('A-015 →, oda 2');
    expect(el.textContent).not.toContain('Henüz çağrı yok');
  });

  it('ilk açılıştaki çağrı vurgulanmaz, sonra gelen yeni çağrı vurgulanır', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').flush(BRANSLAR);
    http.expectOne('/api/tv/cagrilar').flush([cagri('A-016', '3', 'M.Y.', '2026-10-31T11:10:00Z')]);
    await fixture.whenStable();
    expect(el.querySelector('[data-test="son-cagri"]')!.classList).not.toContain('vurgu');

    (fixture.componentInstance as unknown as { cagrilar: { reload(): void } }).cagrilar.reload();
    fixture.detectChanges();
    http.expectOne('/api/tv/cagrilar').flush([
      cagri('A-017', '3', 'A.K.', '2026-10-31T11:21:00Z'),
      cagri('A-016', '3', 'M.Y.', '2026-10-31T11:10:00Z'),
    ]);
    await fixture.whenStable();
    expect(el.querySelector('[data-test="son-cagri"]')!.classList).toContain('vurgu');
  });

  it('çağrılar yüklenirken "Yükleniyor…" gösterir', () => {
    const { el } = kur();
    expect(el.querySelector('section')!.textContent).toContain('Yükleniyor…');
  });

  // Hata durumunda httpResource.value() istisna fırlatır; ekran "Yükleniyor…"da takılmamalı.
  it('veri alınamazsa hata durumunu gösterir', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').error(new ProgressEvent('error'));
    http.expectOne('/api/tv/cagrilar').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(el.textContent).toContain('Bağlantı bekleniyor');
    expect(el.textContent).not.toContain('Yükleniyor');
    expect(el.textContent).not.toContain('Henüz çağrı yok');
  });
});
