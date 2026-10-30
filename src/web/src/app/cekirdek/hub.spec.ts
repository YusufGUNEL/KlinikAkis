import { TestBed } from '@angular/core/testing';
import { HUB_BAGLANTISI, HubService } from './hub';
import { sahteBaglanti } from './hub.sahte';
import { Oturum } from './oturum';

function kur(token: string | null = 'tkn') {
  const sahte = sahteBaglanti();
  let uretimSayisi = 0;
  TestBed.configureTestingModule({
    providers: [
      { provide: Oturum, useValue: { token: () => token } },
      { provide: HUB_BAGLANTISI, useValue: () => (uretimSayisi++, sahte.baglanti) },
    ],
  });
  return { hub: TestBed.inject(HubService), sahte, uretim: () => uretimSayisi };
}

describe('HubService', () => {
  it('bağlanınca canlı olur, olayda ve yeniden bağlanınca değişiklik sayacı artar', async () => {
    const { hub, sahte } = kur();
    hub.baglan();
    await new Promise((r) => setTimeout(r));
    expect(hub.durum()).toBe('canli');

    const once = hub.degisiklik();
    sahte.olayGonder('RandevuAlindi', 'r1');
    expect(hub.degisiklik()).toBe(once + 1);
    sahte.yenidenBagla();
    expect(hub.degisiklik()).toBe(once + 2);
  });

  it('kopunca durumu "koptu" yapar', async () => {
    const { hub, sahte } = kur();
    hub.baglan();
    await new Promise((r) => setTimeout(r));
    sahte.kopar();
    expect(hub.durum()).toBe('koptu');
  });

  it('aynı oturumla ikinci baglan() yeni bağlantı açmaz; oturum yoksa hiç açmaz', () => {
    const { hub, uretim } = kur();
    hub.baglan();
    hub.baglan();
    expect(uretim()).toBe(1);

    TestBed.resetTestingModule();
    const yok = kur(null);
    yok.hub.baglan();
    expect(yok.uretim()).toBe(0);
  });
});
