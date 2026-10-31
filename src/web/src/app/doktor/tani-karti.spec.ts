import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { MuayeneDetayi, TaniOnerisiKarti } from '../cekirdek/api';
import { TaniKarti } from './tani-karti';

const oneri = (ek: Partial<TaniOnerisiKarti> = {}): TaniOnerisiKarti => ({
  id: 't1',
  durum: 'Taslak',
  kaynak: 'Llm',
  model: 'gemini-3.5-flash-lite',
  zaman: '2026-10-29T11:10:00Z',
  kotaDoldu: false,
  oneriler: [
    { kod: 'E11.9', ad: 'Tip 2 diabetes mellitus, komplikasyonsuz', gerekce: 'Kayıttaki kronik hastalık: Tip 2 diyabet.' },
    { kod: 'R73.9', ad: 'Hiperglisemi, tanımlanmamış', gerekce: 'HbA1c ▲ Y (işaret kodla hesaplandı).' },
  ],
  karar: null,
  ...ek,
});

const muayene = (taniOnerisi: TaniOnerisiKarti | null, ek: Partial<MuayeneDetayi> = {}): MuayeneDetayi => ({
  id: 'm1',
  randevuId: 'r1',
  durum: 'Acik',
  hekimNotu: null,
  epikriz: null,
  tanilar: [],
  taniOnerisi,
  ...ek,
});

@Component({
  imports: [TaniKarti],
  template: `<app-tani-karti [detay]="detay()" />`,
})
class Kap {
  readonly detay = signal<MuayeneDetayi>(muayene(null));
}

function kur(d: MuayeneDetayi) {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Kap);
  fixture.componentInstance.detay.set(d);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, http: TestBed.inject(HttpTestingController), kartEl: () => el.querySelector('[data-test="ai-karti"]') };
}

const buton = (el: HTMLElement, metin: string) =>
  Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.trim() === metin) as HTMLButtonElement | undefined;

describe('TaniKarti', () => {
  it('öneri ve tanı yoksa boş durum ve "ICD-10 önerisi oluştur" görünür', () => {
    const { el, kartEl } = kur(muayene(null));
    expect(kartEl()).toBeNull();
    expect(el.querySelector('[data-test="tani-yok"]')!.textContent).toContain('Tanı kodu girilmedi.');
    expect(el.querySelector('[data-test="tani-uret"]')!.textContent).toContain('ICD-10 önerisi oluştur');
    expect(el.textContent).toContain('tanıyı hekim koyar');
  });

  it('taslak: mor kesik kart, kod · ad · gerekçe, Onayla/Düzenle/Reddet', () => {
    const { el, kartEl } = kur(muayene(oneri()));
    expect(kartEl()!.classList).toContain('border-dashed');
    expect(el.textContent).toContain('AI taslağı · klinik karar hekime aittir · gemini-3.5-flash-lite');
    expect(el.querySelector('[data-test="tani-onerileri"]')!.textContent).toContain('E11.9 · Tip 2 diabetes mellitus, komplikasyonsuz');
    expect(el.textContent).toContain('HbA1c ▲ Y (işaret kodla hesaplandı).');
    expect(buton(el, 'Onayla')).toBeDefined();
    expect(buton(el, 'Düzenle')).toBeDefined();
    expect(el.querySelector('[data-test="tani-uret"]')).toBeNull();
  });

  it('onay sonrası kenar düz, onaylı kodlar listede, butonlar yok', () => {
    const { el, kartEl } = kur(
      muayene(oneri({ durum: 'Onaylandi', karar: { durum: 'Onaylandi', hekimAd: 'Uzm. Dr. Selin Arslan', zaman: '2026-10-29T11:32:00Z' } }), {
        tanilar: [{ kod: 'E11.9', ad: 'Tip 2 diabetes mellitus, komplikasyonsuz' }],
      }),
    );
    expect(kartEl()!.classList).toContain('border-solid');
    expect(el.querySelector('[data-test="ai-onay"]')!.textContent).toContain('Uzm. Dr. Selin Arslan');
    expect(el.querySelector('[data-test="tanilar"]')!.textContent).toContain('E11.9');
    expect(buton(el, 'Reddet')).toBeUndefined();
    expect(el.querySelector('[data-test="ai-onay"]')!.textContent).toContain('düzenleyip onayladı');
  });

  it('yedek öneri boşsa Onayla yok, Düzenle var', () => {
    const { el } = kur(muayene(oneri({ kaynak: 'Yedek', model: null, oneriler: [] })));
    expect(el.textContent).toContain('Otomatik taslak · model yanıt vermedi, kural tabanlı');
    expect(el.querySelector('[data-test="tani-oneri-yok"]')).not.toBeNull();
    expect(buton(el, 'Onayla')).toBeUndefined();
    expect(buton(el, 'Düzenle')).toBeDefined();
  });

  it('kapalı vizitte karar ve öneri butonu yok', () => {
    const { el } = kur(muayene(oneri(), { durum: 'Kapali' }));
    expect(buton(el, 'Onayla')).toBeUndefined();
    expect(el.querySelector('[data-test="tani-uret"]')).toBeNull();
  });

  it('Onayla: karar isteği önerilen kodlarla değil yalnız kararla gider', async () => {
    const { fixture, el, http } = kur(muayene(oneri()));
    buton(el, 'Onayla')!.click();
    const istek = http.expectOne({ method: 'POST', url: '/api/tani-onerisi/t1/karar' });
    expect(istek.request.body).toEqual({ karar: 'onayla', kodlar: null });
    istek.flush(muayene(oneri({ durum: 'Onaylandi' })));
    await fixture.whenStable();
  });
});
