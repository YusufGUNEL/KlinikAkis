import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { CevapYaniti, OzetYaniti, SohbetSorusu } from '../cekirdek/api';
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
    // 112 alanı var ama şimdilik aramaz: yanlışlıkla dokunulunca telefon çalmasın.
    expect(bant.textContent).toContain("112'yi arayın");
    expect(el.querySelector('a[href^="tel:"]')).toBeNull();
    expect(el.querySelector('#cevap-metni')).toBeNull();
    expect(el.textContent).not.toContain('Randevu saatini seçin');
  });

  it('sohbet bitince AI kartında özet ve önerilen bölüm; Doğru deyince önerilen bölümle randevuya geçilir', async () => {
    const { fixture, http, el } = await kur();
    yaz(el, 'çok su içiyorum');
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/onanamnez/o1/cevap').flush({ sonrakiSoru: null, adim: 6, toplamAdim: 6, kirmiziBayrak: null } satisfies CevapYaniti);
    await bekle();
    http.expectOne('/api/onanamnez/o1/ozet').flush({
      onAnamnezId: 'o1',
      durum: 'Taslak',
      kaynak: 'Llm',
      kirmiziBayrak: null,
      kart: {
        id: 'o1',
        durum: 'Taslak',
        zaman: '2026-10-29T10:52:00Z',
        cevaplar: [
          { adim: 1, soru: 'Şikayetiniz nedir?', cevap: 'çok su içiyorum' },
          { adim: 2, soru: 'Ne kadar süredir?', cevap: 'Daha uzun' },
        ],
        onerilen: { bransId: 'b-endo', kod: 'Endokrinoloji', ad: 'Endokrinoloji' },
        alternatif: { bransId: 'b-dah', kod: 'Dahiliye', ad: 'Dahiliye' },
      },
    } satisfies OzetYaniti);
    await bekle();
    fixture.detectChanges();

    const kart = el.querySelector('[data-test="ai-karti"]')!;
    expect(kart.classList).toContain('border-dashed');
    expect(el.querySelector('[data-test="onerilen"]')!.textContent).toContain('Endokrinoloji');
    const cevaplar = el.querySelector('[data-test="cevaplar"]')!.textContent;
    expect(cevaplar).toContain('Şikayet');
    expect(cevaplar).toContain('çok su içiyorum');
    expect(el.querySelector('[data-test="ai-baslik"]')!.textContent).toContain('tanı değildir');
    expect(el.querySelector('[data-test="ai-kota"]')).toBeNull(); // kota iç işleyiştir, hastaya gösterilmez
    expect(el.querySelector('[data-test="randevu-onerilen"]')).toBeNull();

    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Doğru'))!.click();
    const onay = http.expectOne('/api/onanamnez/o1/hasta-onayi');
    expect(onay.request.body).toEqual({ dogru: true, not: null });
    onay.flush({});
    await bekle();
    fixture.detectChanges();

    const baglanti = el.querySelector<HTMLAnchorElement>('[data-test="randevu-onerilen"]')!;
    expect(baglanti.textContent).toContain('Endokrinoloji için saat seçin');
    expect(baglanti.getAttribute('href')).toContain('brans=b-endo');
    expect(baglanti.getAttribute('href')).toContain('onAnamnez=o1');
  });
});
