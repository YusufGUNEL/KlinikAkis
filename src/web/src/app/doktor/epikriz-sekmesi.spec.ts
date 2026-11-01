import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { EpikrizKarti, MuayeneDetayi } from '../cekirdek/api';
import { EpikrizSekmesi, epikrizMetni } from './epikriz-sekmesi';

const kart = (ek: Partial<EpikrizKarti> = {}): EpikrizKarti => ({
  id: 'e1',
  durum: 'Taslak',
  kaynak: 'Llm',
  model: 'gemini-3.8-flash',
  zaman: '2026-10-29T11:10:00Z',
  kotaDoldu: false,
  taslak: {
    oyku: 'Birkaç haftadır susama ve sık idrar.',
    degerlendirme: 'Ağız kuruluğu. Tanı hekim tarafından belirlenecek.',
    plan: 'Plan hekim tarafından belirlenecek.',
    alerji: 'Penisilin',
    tahliller: ['HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y'],
  },
  sonMetin: null,
  karar: null,
  ...ek,
});

const muayene = (epikriz: EpikrizKarti | null): MuayeneDetayi => ({
  id: 'm1',
  randevuId: 'r1',
  durum: 'Acik',
  hekimNotu: 'Ağız kuruluğu',
  epikriz,
  tanilar: [],
  taniOnerisi: null,
});

@Component({
  imports: [EpikrizSekmesi],
  template: `<app-epikriz-sekmesi [detay]="detay()" />`,
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

describe('EpikrizSekmesi', () => {
  it('taslak yoksa oluştur butonu ve kayıtlı doktor notu görünür', () => {
    const { el, kartEl } = kur(muayene(null));
    expect(kartEl()).toBeNull();
    expect(el.querySelector('[data-test="epikriz-uret"]')!.textContent).toContain('Epikriz taslağı oluştur');
    expect((el.querySelector('#hekim-notu') as HTMLTextAreaElement).value).toBe('Ağız kuruluğu');
  });

  it('taslak: mor kesik kart, alerji kırmızı ve [kayıttan], tahlil satırı kodla', () => {
    const { el, kartEl } = kur(muayene(kart()));
    expect(kartEl()!.classList).toContain('border-dashed');
    expect(el.textContent).toContain('AI taslağı · klinik karar hekime aittir · gemini-3.8-flash');
    const alerji = el.querySelector('[data-test="epikriz-alerji"]')!;
    expect(alerji.querySelector('.text-tehlike')!.textContent).toContain('Penisilin');
    expect(alerji.textContent).toContain('[kayıttan]');
    expect(el.textContent).toContain('HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y');
    expect(el.textContent).toContain('Onayla');
    expect(el.querySelector('[data-test="epikriz-uret"]')).toBeNull();
  });

  it('onay sonrası kenar düz, hekim adı ve butonlar yok', () => {
    const { el, kartEl } = kur(
      muayene(kart({ durum: 'Onaylandi', karar: { durum: 'Onaylandi', hekimAd: 'Uzm. Dr. Selin Arslan', zaman: '2026-10-29T11:32:00Z' } })),
    );
    expect(kartEl()!.classList).toContain('border-solid');
    expect(el.querySelector('[data-test="ai-onay"]')!.textContent).toContain('Uzm. Dr. Selin Arslan onayladı');
    expect(el.textContent).not.toContain('Reddet');
  });

  it('onaylı epikrizde doktor notu kilitli', () => {
    const { el } = kur(
      muayene(kart({ durum: 'Onaylandi', karar: { durum: 'Onaylandi', hekimAd: 'Uzm. Dr. Selin Arslan', zaman: '2026-10-29T11:32:00Z' } })),
    );
    expect((el.querySelector('#hekim-notu') as HTMLTextAreaElement).disabled).toBe(true);
    expect(el.querySelector('[data-test="not-kilitli"]')!.textContent).toContain('Epikriz onaylandı');
  });

  it('taslak epikrizde doktor notu açık', () => {
    const { el } = kur(muayene(kart()));
    expect((el.querySelector('#hekim-notu') as HTMLTextAreaElement).disabled).toBe(false);
    expect(el.querySelector('[data-test="not-kilitli"]')).toBeNull();
  });

  it('kapalı vizitte taslak kalsa da karar ve oluştur butonu yok', () => {
    const { el } = kur({ ...muayene(kart()), durum: 'Kapali' });
    expect(el.textContent).not.toContain('Onayla');
    expect(el.querySelector('[data-test="epikriz-uret"]')).toBeNull();
    expect(el.querySelector('[data-test="epikriz-kapali"]')!.textContent).toContain('onaylanmadan kapatıldı');
  });

  it('düzenlenmiş epikrizde alerji ve tahlil satırları yine koddan, kırmızı ve [kayıttan] ile görünür', () => {
    const { el } = kur(
      muayene(
        kart({
          durum: 'Onaylandi',
          sonMetin: 'Öykü: Susama.\nDeğerlendirme: Ağız kuruluğu.\nPlan: 3 ay sonra kontrol.',
          karar: { durum: 'Onaylandi', hekimAd: 'Uzm. Dr. Selin Arslan', zaman: '2026-10-29T11:32:00Z' },
        }),
      ),
    );
    expect(el.querySelector('[data-test="epikriz-son-metin"]')!.textContent).toContain('3 ay sonra kontrol');
    const alerji = el.querySelector('[data-test="epikriz-alerji"]')!;
    expect(alerji.querySelector('.text-tehlike')!.textContent).toContain('Penisilin');
    expect(alerji.textContent).toContain('[kayıttan]');
    expect(el.textContent).toContain('HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y');
    expect(el.querySelector('[data-test="ai-onay"]')!.textContent).toContain('düzenleyip onayladı');
  });

  it('düzenleme kutusunda alerji ve tahlil satırı yok', () => {
    const metin = epikrizMetni(kart().taslak);
    expect(metin).toContain('Plan: Plan hekim tarafından belirlenecek.');
    expect(metin).not.toContain('Penisilin');
    expect(metin).not.toContain('HbA1c');
  });

  it('alerji yoksa kırmızı kullanılmaz', () => {
    const { el } = kur(muayene(kart({ taslak: { ...kart().taslak, alerji: 'Bilinen alerji yok' } })));
    const alerji = el.querySelector('[data-test="epikriz-alerji"]')!;
    expect(alerji.textContent).toContain('Bilinen alerji yok');
    expect(alerji.querySelector('.text-tehlike')).toBeNull();
  });

  it('oluştur: istek atılır, hata Türkçe gösterilir', async () => {
    const { fixture, el, http } = kur(muayene(null));
    (el.querySelector('[data-test="epikriz-uret"]') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: '/api/muayeneler/m1/epikriz' })
      .flush({ detail: 'Bu işlem için yetkiniz yok.' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Bu işlem için yetkiniz yok.');
  });
});
