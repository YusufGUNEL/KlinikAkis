import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { CevapYaniti, SohbetSorusu } from '../cekirdek/api';
import { Sikayet } from './sikayet';

const soru = (adim: number, secenekler: string[] = []): SohbetSorusu => ({
  adim,
  metin: `Soru ${adim}`,
  secenekler,
  cokluSecim: false,
  atlanabilir: false,
});

const bekle = () => new Promise((r) => setTimeout(r));

async function kur() {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Sikayet);
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/onanamnez').flush({ onAnamnezId: 'o1', ilkSoru: soru(1, ['Ateş']), toplamAdim: 6 });
  await bekle();
  fixture.detectChanges();
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

function yaz(el: HTMLElement, metin: string): void {
  const giris = el.querySelector<HTMLInputElement>('#cevap-metni')!;
  giris.value = metin;
  giris.dispatchEvent(new Event('input'));
}

describe('Sikayet', () => {
  it('adım sayacı ve hızlı buton cevabı gönderir', async () => {
    const { fixture, http, el } = await kur();
    expect(el.textContent).toContain('Adım 1/6');

    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Ateş')!.click();
    const istek = http.expectOne('/api/onanamnez/o1/cevap');
    expect(istek.request.body).toEqual({ secimler: ['Ateş'], metin: null });
    istek.flush({ sonrakiSoru: soru(2), adim: 1, toplamAdim: 6, kirmiziBayrak: null } satisfies CevapYaniti);
    await bekle();
    fixture.detectChanges();

    expect(el.textContent).toContain('Adım 2/6');
    expect(el.textContent).toContain('Cevabınız: Ateş');
  });

  it('kırmızı bayrakta 112 bandı çıkar, yazı kutusu kapanır', async () => {
    const { fixture, http, el } = await kur();
    yaz(el, 'göğsüm ağrıyor, terledim');
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    http.expectOne('/api/onanamnez/o1/cevap').flush({
      sonrakiSoru: null,
      adim: 1,
      toplamAdim: 6,
      kirmiziBayrak: { kural: 'GogusAgrisiTerleme', mesaj: "Belirtileriniz acil olabilir. Hemen 112'yi arayın." },
    } satisfies CevapYaniti);
    await bekle();
    fixture.detectChanges();

    const bant = el.querySelector('[role=alert]')!;
    expect(bant.textContent).toContain('112');
    expect(bant.querySelector('a[href="tel:112"]')).not.toBeNull();
    expect(el.querySelector('#cevap-metni')).toBeNull();
    expect(el.textContent).not.toContain('Randevu saatini seçin');
  });
});
