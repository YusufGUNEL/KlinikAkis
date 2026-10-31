import { httpResource } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { OutboxSatiri } from '../cekirdek/api';
import { GonderimKutusu } from './gonderim-kutusu';

const paket = (ek: Partial<OutboxSatiri>): OutboxSatiri => ({
  id: 'p1',
  paketTipi: 'HastaKayit',
  hastaTakmaAdi: 'P-7F3A',
  durum: 'Gonderildi',
  deneme: 1,
  zaman: '2026-10-31T11:02:00Z',
  hata: null,
  ...ek,
});

@Component({
  imports: [GonderimKutusu],
  template: `<app-gonderim-kutusu [liste]="outbox" [kompakt]="kompakt()" />`,
})
class Kap {
  readonly kompakt = input(false);
  readonly outbox = httpResource<OutboxSatiri[]>(() => '/api/outbox');
}

async function kur(paketler: OutboxSatiri[]) {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Kap);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/outbox').flush(paketler);
  await fixture.whenStable();
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

describe('GonderimKutusu', () => {
  it('SİMÜLASYON etiketi, paket kodu, takma ad, durum ikon+metin; yalnız hatalı pakette Yeniden dene', async () => {
    const { el } = await kur([
      paket({ id: 'a', paketTipi: 'HastaKayit' }),
      paket({ id: 'b', paketTipi: 'Muayene', durum: 'Bekliyor', deneme: 0 }),
      paket({ id: 'c', paketTipi: 'LabSonuc', durum: 'IsKuraliHatasi', hata: 'Sonuç birimi UCUM biçiminde değil — simülasyon' }),
    ]);
    expect(el.querySelector('h2')!.textContent).toContain('SİMÜLASYON');
    const satirlar = [...el.querySelectorAll('tbody tr')].map((t) => t.textContent!.replace(/\s+/g, ' '));
    expect(satirlar[0]).toContain('101 Hasta Kayıt');
    expect(satirlar[0]).toContain('P-7F3A');
    expect(satirlar[0]).toContain('✓ Gönderildi');
    expect(satirlar[1]).toContain('◌ Bekliyor');
    expect(satirlar[2]).toContain('⚠ İş kuralı');
    expect(satirlar[2]).toContain('UCUM');
    const butonlar = [...el.querySelectorAll('button')].filter((b) => b.textContent!.includes('Yeniden dene'));
    expect(butonlar.length).toBe(1);
    expect(butonlar[0].getAttribute('aria-label')).toBe('105 Lab Sonuç P-7F3A: yeniden dene');
    // Kırmızı (tehlike) yalnız klinik uyarıya ayrılı; iş kuralı hatası amber.
    expect(el.querySelector('.text-tehlike')).toBeNull();
    expect(el.querySelector('.text-uyari')!.textContent).toContain('İş kuralı');
  });

  it('Yeniden dene isteği gönderir ve listeyi yeniden çeker', async () => {
    const { fixture, http, el } = await kur([paket({ id: 'c', paketTipi: 'LabSonuc', durum: 'IsKuraliHatasi', hata: 'UCUM' })]);
    (el.querySelector('tbody button') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: '/api/outbox/c/yeniden-dene' }).flush(paket({ id: 'c', paketTipi: 'LabSonuc', durum: 'Bekliyor' }));
    // whenStable burada beklenmez: reload'un açık isteği flush edilmeden kararlı olmaz.
    await new Promise((r) => setTimeout(r));
    fixture.detectChanges();
    http.expectOne('/api/outbox').flush([paket({ id: 'c', paketTipi: 'LabSonuc', durum: 'Gonderildi', deneme: 2 })]);
    await fixture.whenStable();
    expect(el.querySelector('tbody tr')!.textContent).toContain('✓ Gönderildi');
    expect(el.textContent).not.toContain('Yeniden dene');
  });

  it('kompakt modda hata küçük satırla gösterilir', async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(Kap);
    fixture.componentRef.setInput('kompakt', true);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/outbox').error(new ProgressEvent('error'));
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Yüklenemedi.');
    expect(el.querySelector('app-durum-kutusu')).toBeNull();
    expect(el.querySelector('h2')!.textContent).toContain('SİMÜLASYON');
  });

  it('boş listede mesaj gösterir', async () => {
    const { el } = await kur([]);
    expect(el.textContent).toContain('Henüz paket yok.');
  });
});
