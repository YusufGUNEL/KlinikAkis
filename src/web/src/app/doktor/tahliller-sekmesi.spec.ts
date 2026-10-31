import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { Isaret, TahlilSatiri } from '../cekirdek/tahlil';
import { TahlillerSekmesi } from './tahliller-sekmesi';

const satir = (isaret: Isaret, deger: number, taslak: string | null): TahlilSatiri => ({
  tahlilTanimiId: 't1',
  ad: 'Hemoglobin',
  hastaDostuAd: 'Kan değeri',
  loincKodu: '718-7',
  birim: 'g/dL',
  altSinir: 12,
  ustSinir: 15.5,
  kritikAlt: 7,
  kritikUst: 20,
  ondalik: 1,
  not: 'Örnek aralık',
  son: { id: 's1', deger, zaman: '2026-10-29T05:25:00Z', isaret, hastayaYayinlandi: false },
  trend: [{ id: 's1', deger, zaman: '2026-10-29T05:25:00Z', isaret, hastayaYayinlandi: false }],
  aciklama: {
    kaynak: taslak ? 'Llm' : null,
    taslak,
    model: taslak ? 'gemini-3.5-flash-lite' : null,
    zaman: taslak ? '2026-10-29T10:52:00Z' : null,
    kotaDoldu: false,
    yayinlanan: null,
    yayinZamani: null,
    yayinlayanHekim: null,
  },
});

@Component({
  imports: [TahlillerSekmesi],
  template: `<app-tahliller-sekmesi [satirlar]="satirlar()" />`,
})
class Kap {
  readonly satirlar = signal<TahlilSatiri[]>([]);
}

const bekle = () => new Promise((r) => setTimeout(r));

function kur(s: TahlilSatiri) {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Kap);
  fixture.componentInstance.satirlar.set([s]);
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

function buton(metin: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll('button')].find((b) => b.textContent!.trim() === metin);
}

describe('TahlillerSekmesi', () => {
  afterEach(() => document.querySelectorAll('.p-dialog-mask').forEach((m) => m.remove()));

  it('işaret koddan gelen etiketle, kritikte kırmızı; AI taslağı mor kesik kartta', () => {
    const { el } = kur(satir('KritikDusuk', 6.8, 'Kan değeriniz çok düşük.'));
    const isaret = el.querySelector('[data-test="isaret"]')!;
    expect(isaret.textContent).toContain('‼ KD');
    expect(isaret.classList).toContain('text-tehlike');
    expect(el.textContent).toContain('6,8');
    expect(el.querySelector('[data-test="ai-karti"]')!.classList).toContain('border-dashed');
    expect(el.querySelector('svg[role="img"]')!.getAttribute('aria-label')).toBe('Trend: 6,8');
  });

  it('kritik değer: yayınla önce pencere açar, seçim yokken Yayınla pasif; seçimle bilgilendirme gönderilir', async () => {
    const { fixture, http } = kur(satir('KritikDusuk', 6.8, 'Kan değeriniz çok düşük.'));

    buton('Hastaya yayınla')!.click();
    fixture.detectChanges();
    await bekle();
    fixture.detectChanges();
    http.expectNone('/api/tahliller/yayinla');

    const yayinla = document.querySelector<HTMLButtonElement>('[data-test="kritik-yayinla"]')!;
    expect(document.querySelector('[data-test="kritik-deger"]')!.textContent).toContain('Hemoglobin 6,8 g/dL');
    expect(yayinla.disabled).toBe(true);

    const secenek = document.querySelector<HTMLInputElement>('input[value="TelefonlaBilgilendirildi"]')!;
    secenek.click();
    fixture.detectChanges();
    expect(yayinla.disabled).toBe(false);

    yayinla.click();
    const istek = http.expectOne('/api/tahliller/yayinla');
    expect(istek.request.body).toEqual({
      sonuclar: [{ sonucId: 's1', aciklama: 'Kan değeriniz çok düşük.' }],
      bilgilendirme: 'TelefonlaBilgilendirildi',
    });
  });

  it('kritik olmayan sonuç düzenlenip pencere olmadan yayınlanır', async () => {
    const { fixture, http, el } = kur(satir('Yuksek', 16.1, 'Taslak metin.'));

    buton('Düzenle')!.click();
    fixture.detectChanges();
    const alan = el.querySelector<HTMLTextAreaElement>('textarea')!;
    alan.value = 'Hekimin düzenlediği metin.';
    alan.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    buton('Düzenlenmiş hâliyle yayınla')!.click();

    const istek = http.expectOne('/api/tahliller/yayinla');
    expect(istek.request.body).toEqual({
      sonuclar: [{ sonucId: 's1', aciklama: 'Hekimin düzenlediği metin.' }],
      bilgilendirme: null,
    });
    expect(document.querySelector('[data-test="kritik-yayinla"]')).toBeNull();
  });

  it('taslak yoksa üretme butonu; reddedilen taslak kart olarak görünmez', async () => {
    const { fixture, http, el } = kur(satir('Yuksek', 16.1, null));
    el.querySelector<HTMLButtonElement>('[data-test="taslak-uret-718-7"]')!.click();
    http.expectOne('/api/tahliller/s1/aciklama').flush(satir('Yuksek', 16.1, 'Taslak metin.').aciklama);
    await bekle();

    fixture.componentInstance.satirlar.set([satir('Yuksek', 16.1, 'Taslak metin.')]);
    fixture.detectChanges();
    buton('Reddet')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[data-test="ai-karti"]')).toBeNull();
    expect(el.textContent).toContain('Taslak reddedildi');
  });
});
