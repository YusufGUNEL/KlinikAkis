import { TestBed } from '@angular/core/testing';
import { HastaOzeti } from '../cekirdek/api';
import { HastaBandi } from './hasta-bandi';

const AYSE: HastaOzeti = {
  id: 'a1',
  ad: 'Ayşe',
  soyad: 'Kaya',
  yas: 54,
  cinsiyet: 'K',
  tcMaskeli: '99*******78',
  takmaAd: 'P-7F3A',
  alerjiler: ['Penisilin'],
  kronikHastaliklar: ['Tip 2 DM'],
  kullandigiIlaclar: ['Metformin'],
  acikRizaAlindi: true,
  sentetik: true,
};

function ciz(ozet: HastaOzeti): HTMLElement {
  const fixture = TestBed.createComponent(HastaBandi);
  fixture.componentRef.setInput('ozet', ozet);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// SPEC §11 test 12 (Aşama 9'da listelenir; bileşenle birlikte yazıldı).
describe('HastaBandi', () => {
  it('alerji doluysa kırmızı + ikon + metinle gösterir', () => {
    const alerji = ciz(AYSE).querySelector('[data-test="alerji"]')!;
    expect(alerji.textContent).toContain('Alerji: Penisilin');
    expect(alerji.textContent).toContain('⛔');
    expect(alerji.classList).toContain('text-tehlike');
  });

  it('alerji boşsa "Bilinen alerji yok" yazar, kırmızı kullanmaz', () => {
    const el = ciz({ ...AYSE, alerjiler: [] });
    const alerji = el.querySelector('[data-test="alerji"]')!;
    expect(alerji.textContent).toContain('Bilinen alerji yok');
    expect(el.querySelector('.text-tehlike')).toBeNull();
  });

  it('maskeli TC, yaş/cinsiyet, ilaç sayısı ve SENTETİK rozetini gösterir', () => {
    const metin = ciz(AYSE).textContent!;
    expect(metin).toContain('Ayşe K.');
    expect(metin).toContain('54 K');
    expect(metin).toContain('TC 99*******78');
    expect(metin).toContain('1 ilaç');
    expect(metin).toContain('SENTETİK');
  });
});
