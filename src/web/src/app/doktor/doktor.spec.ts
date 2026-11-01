import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { OnAnamnezDetayi, RandevuSatiri } from '../cekirdek/api';
import { HUB_BAGLANTISI } from '../cekirdek/hub';
import { sahteBaglanti } from '../cekirdek/hub.sahte';
import { Oturum } from '../cekirdek/oturum';
import { Doktor } from './doktor';

// jsdom'da ResizeObserver yok; PrimeNG Tabs sekme çubuğunu ölçmek için kullanır.
globalThis.ResizeObserver ??= class {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
} as unknown as typeof ResizeObserver;

const satir = (durum: RandevuSatiri['durum'], izinliEylemler: RandevuSatiri['izinliEylemler']): RandevuSatiri => ({
  id: 'r1',
  baslangic: '2026-10-29T11:10:00Z',
  siraNo: 'A-017',
  durum,
  hastaId: 'h1',
  hastaAd: 'Ayşe K.',
  bransAd: 'Endokrinoloji',
  hekimAd: 'Uzm. Dr. Selin Arslan',
  oda: '3',
  acikRizaAlindi: true,
  takipNo: 'SIM-000001',
  izinliEylemler,
  onAnamnezId: null,
  muayeneId: null,
});

const detay = (durum: OnAnamnezDetayi['durum'], karar: OnAnamnezDetayi['karar'] = null): OnAnamnezDetayi => ({
  id: 'o1',
  hastaId: 'h1',
  durum,
  kirmiziBayrakKurali: null,
  sohbet: [{ adim: 1, soru: 'Şikayetiniz nedir?', secimler: [], metin: 'çok su içiyorum', cevap: 'çok su içiyorum' }],
  hastaOnayi: true,
  hastaNotu: null,
  karar,
  kart: {
    id: 'o1',
    durum,
    kaynak: 'Llm',
    model: 'gemini-3.5-flash-lite',
    zaman: '2026-10-29T10:52:00Z',
    kotaDoldu: false,
    onerilen: { bransId: 'b1', kod: 'Endokrinoloji', ad: 'Endokrinoloji' },
    alternatif: { bransId: 'b2', kod: 'Dahiliye', ad: 'Dahiliye' },
    taslak: {
      ozet: 'Birkaç haftadır susama ve sık idrar.',
      semptomlar: ['Susama'],
      sure: 'Haftalardır',
      siddet: 'Orta',
      onerilenBrans: 'Endokrinoloji',
      alternatifBrans: 'Dahiliye',
      gerekce: 'Bilinen diyabet.',
      aciliyet: 'Rutin',
      doktoraSorular: [],
      kaynaklar: [
        { madde: 'Susama', sohbetAdimi: 1 },
        { madde: 'Kilo kaybı', sohbetAdimi: 0 },
      ],
    },
  },
});

async function kur() {
  const sahte = sahteBaglanti();
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: HUB_BAGLANTISI, useValue: () => sahte.baglanti },
      { provide: Oturum, useValue: { token: () => 'tkn', aktif: () => null } },
    ],
  });
  const fixture = TestBed.createComponent(Doktor);
  fixture.detectChanges();
  // Bağlantı kurulunca liste bir kez daha çekilir (kopukken kaçanlar için); ikisi de beklenen.
  await new Promise((r) => setTimeout(r));
  TestBed.tick();
  return { fixture, sahte, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

/** İptal edilmemiş bekleyen liste isteklerini yanıtlar. */
function listeyiVer(http: HttpTestingController, satirlar: RandevuSatiri[]): void {
  const istekler = http.match('/api/randevular').filter((i) => !i.cancelled);
  expect(istekler.length).toBeGreaterThan(0);
  istekler.forEach((i) => i.flush(satirlar));
}

const bekle = () => new Promise((r) => setTimeout(r));

function buton(el: HTMLElement, metin: string): HTMLButtonElement {
  return [...el.querySelectorAll('button')].find((b) => b.textContent!.trim() === metin)!;
}

describe('Doktor', () => {
  it('boş listede yaklaşan başlığı ve demo sıfırlama düğmesi gösterir', async () => {
    const { fixture, http, el } = await kur();
    listeyiVer(http, []);
    await fixture.whenStable();

    expect(el.textContent).toContain('Yaklaşan randevular');
    expect(el.textContent).toContain('Yaklaşan randevu yok.');
    expect(buton(el, 'Demo verisini sıfırla')).toBeTruthy();
  });

  it('eylem butonları sunucunun izinliEylemler listesine göre etkinleşir', async () => {
    const { fixture, http, el } = await kur();
    listeyiVer(http, [satir('KabulEdildi', ['Cagir'])]);
    await fixture.whenStable();

    el.querySelector<HTMLButtonElement>('nav li button')!.click();
    TestBed.tick();
    http.expectOne('/api/hastalar/h1/ozet');
    fixture.detectChanges();

    expect(buton(el, 'Hastayı çağır').disabled).toBe(false);
    expect(buton(el, 'Muayeneye başla').disabled).toBe(true);
    expect(buton(el, 'Vizit kapat').disabled).toBe(true);
  });

  it('SignalR olayı gelince listeyi yeniden çeker', async () => {
    const { fixture, http, sahte } = await kur();
    listeyiVer(http, []);
    await fixture.whenStable();

    sahte.olayGonder('RandevuAlindi', 'r1');
    TestBed.tick();
    listeyiVer(http, [satir('Planlandi', [])]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Ayşe K.');
  });

  it('ön anamnez kartı: kaynak adımı ve "kaynak yok"; Onayla sonrası kenar düz, "onayladı"', async () => {
    const { fixture, http, el } = await kur();
    listeyiVer(http, [{ ...satir('KabulEdildi', ['Cagir']), onAnamnezId: 'o1' }]);
    await fixture.whenStable();

    el.querySelector<HTMLButtonElement>('nav li button')!.click();
    TestBed.tick();
    http.expectOne('/api/onanamnez/o1').flush(detay('Taslak'));
    await bekle();
    fixture.detectChanges();

    const kart = el.querySelector('[data-test="ai-karti"]')!;
    expect(kart.classList).toContain('border-dashed');
    expect(kart.textContent).toContain('gemini-3.5-flash-lite');
    expect(kart.textContent).toContain('1. adım');
    expect(el.querySelector('[data-test="kaynak-yok"]')).not.toBeNull();

    buton(el, 'Onayla').click();
    const istek = http.expectOne('/api/onanamnez/o1/karar');
    expect(istek.request.body).toEqual({ karar: 'onayla', metin: null });
    istek.flush(detay('Onaylandi'));
    await bekle();
    TestBed.tick();
    http
      .match('/api/onanamnez/o1')
      .filter((i) => !i.cancelled)
      .forEach((i) => i.flush(detay('Onaylandi', { durum: 'Onaylandi', hekimAd: 'Uzm. Dr. Selin Arslan', zaman: '2026-10-29T11:32:00Z', duzeltme: null })));
    await bekle();
    fixture.detectChanges();

    const sonra = el.querySelector('[data-test="ai-karti"]')!;
    expect(sonra.classList).toContain('border-solid');
    expect(el.querySelector('[data-test="ai-onay"]')!.textContent).toContain('Uzm. Dr. Selin Arslan onayladı');
    expect(buton(el, 'Onayla')).toBeUndefined();
  });

  it('tahliller alınamazsa Uyarılar sütunu "Uyarı yok" demez', async () => {
    const { fixture, http, el } = await kur();
    listeyiVer(http, [satir('KabulEdildi', ['Cagir'])]);
    await fixture.whenStable();

    el.querySelector<HTMLButtonElement>('nav li button')!.click();
    TestBed.tick();
    http
      .match('/api/hastalar/h1/tahliller')
      .filter((i) => !i.cancelled)
      .forEach((i) => i.flush('hata', { status: 500, statusText: 'Server Error' }));
    await bekle();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="uyarilar-hata"]')!.textContent).toContain('uyarılar gösterilemiyor');
    expect(el.textContent).not.toContain('Uyarı yok.');
  });
});
