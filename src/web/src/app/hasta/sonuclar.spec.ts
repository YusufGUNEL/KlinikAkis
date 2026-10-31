import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { appConfig } from '../app.config';
import { HUB_BAGLANTISI } from '../cekirdek/hub';
import { sahteBaglanti } from '../cekirdek/hub.sahte';
import { Oturum } from '../cekirdek/oturum';
import { HastaSonucu } from '../cekirdek/tahlil';
import { Sonuclar } from './sonuclar';

const hba1c: HastaSonucu = {
  sonucId: 's1',
  ad: 'HbA1c',
  hastaDostuAd: 'Şeker ortalaması',
  loincKodu: '4548-4',
  birim: '%',
  altSinir: 4,
  ustSinir: 5.6,
  ondalik: 1,
  not: 'Örnek aralık',
  deger: 7.4,
  zaman: '2026-10-29T05:15:00Z',
  isaret: 'Yuksek',
  aciklama: 'Şeker ortalamanız hedefin üstünde.',
  yayinZamani: '2026-10-29T11:32:00Z',
  yayinlayanHekim: 'Uzm. Dr. Selin Arslan',
  trend: [
    { deger: 6.6, zaman: '2026-04-29T05:15:00Z' },
    { deger: 7, zaman: '2026-07-29T05:15:00Z' },
    { deger: 7.4, zaman: '2026-10-29T05:15:00Z' },
  ],
};

async function kur() {
  const sahte = sahteBaglanti();
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideRouter([]),
      provideHttpClientTesting(),
      { provide: HUB_BAGLANTISI, useValue: () => sahte.baglanti },
      { provide: Oturum, useValue: { token: () => 'tkn', aktif: () => null } },
    ],
  });
  const fixture = TestBed.createComponent(Sonuclar);
  fixture.detectChanges();
  await new Promise((r) => setTimeout(r));
  TestBed.tick();
  return { fixture, sahte, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

function ver(http: HttpTestingController, liste: HastaSonucu[]): void {
  http
    .match('/api/hasta/me/sonuclar')
    .filter((i) => !i.cancelled)
    .forEach((i) => i.flush(liste));
}

describe('Sonuclar', () => {
  it('hasta etiketi, ondalık virgül, trend, onaylı açıklama ve sabit uyarı metni', async () => {
    const { fixture, http, el } = await kur();
    ver(http, [hba1c]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="hasta-etiket"]')!.textContent).toContain('▲ Referansın üstünde');
    expect(el.textContent).toContain('7,4 %');
    expect(el.textContent).toContain('6,6 → 7,0 → 7,4');
    expect(el.textContent).toContain('Referans aralığı: 4,0–5,6 %');
    const kart = el.querySelector('[data-test="ai-karti"]')!;
    expect(kart.textContent).toContain('AI taslağı · tanı değildir');
    expect(kart.textContent).toContain('Uzm. Dr. Selin Arslan onayladı');
    expect(kart.classList).toContain('border-solid');
    expect(kart.textContent).not.toContain('gemini');
    expect(el.querySelector('[data-test="uyari"]')!.textContent).toContain('Bu açıklama bilgilendirme amaçlıdır, tanı yerine geçmez.');
  });

  it('kritik değer kırmızı ve "doktorunuz sizinle iletişime geçecek"; TahlilYayinlandi olayıyla yeniden çekilir', async () => {
    const { fixture, http, el, sahte } = await kur();
    ver(http, []);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('Henüz doktorunuzun paylaştığı bir sonuç yok.');

    sahte.olayGonder('TahlilYayinlandi', 'h1');
    TestBed.tick();
    ver(http, [{ ...hba1c, ad: 'Hemoglobin', deger: 6.8, isaret: 'KritikDusuk', trend: [{ deger: 6.8, zaman: hba1c.zaman }] }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const etiket = el.querySelector('[data-test="hasta-etiket"]')!;
    expect(etiket.textContent).toContain('‼ Çok düşük · doktorunuz sizinle iletişime geçecek');
    expect(etiket.classList).toContain('text-tehlike');
  });
});
