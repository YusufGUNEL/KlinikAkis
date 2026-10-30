import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { RandevuSatiri } from '../cekirdek/api';
import { HUB_BAGLANTISI } from '../cekirdek/hub';
import { sahteBaglanti } from '../cekirdek/hub.sahte';
import { Oturum } from '../cekirdek/oturum';
import { Doktor } from './doktor';

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

function buton(el: HTMLElement, metin: string): HTMLButtonElement {
  return [...el.querySelectorAll('button')].find((b) => b.textContent!.trim() === metin)!;
}

describe('Doktor', () => {
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
});
