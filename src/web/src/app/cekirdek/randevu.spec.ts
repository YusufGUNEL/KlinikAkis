import { HttpErrorResponse } from '@angular/common/http';
import { gunEtiketi, hataMesaji, saatEtiketi } from './randevu';

// Gün sınırı İstanbul'a göre: 20:59Z = 23:59 İstanbul, 21:00Z = ertesi gün 00:00.
const SIMDI = new Date('2026-10-29T20:59:00Z');

describe('gunEtiketi', () => {
  it('aynı İstanbul günü "Bugün" der', () => {
    expect(gunEtiketi('2026-10-29T20:50:00Z', SIMDI)).toBe('Bugün 23:50');
  });

  it('İstanbul gece yarısını geçen saat "Yarın" olur (UTC hâlâ aynı gün olsa da)', () => {
    expect(gunEtiketi('2026-10-29T21:10:00Z', SIMDI)).toBe('Yarın 00:10');
  });

  it('daha ileri gün dd.MM.yyyy HH:mm, tek haneli saat sıfırla', () => {
    expect(gunEtiketi('2026-11-01T06:05:00Z', SIMDI)).toBe('01.11.2026 09:05');
  });

  it('liste satırında bugünün saati yalnızca HH:mm, yarınki önekli', () => {
    expect(saatEtiketi('2026-10-29T20:50:00Z', SIMDI)).toBe('23:50');
    expect(saatEtiketi('2026-10-29T21:10:00Z', SIMDI)).toBe('Yarın 00:10');
  });
});

describe('hataMesaji', () => {
  it('sunucunun Türkçe detail mesajını gösterir', () => {
    const hata = new HttpErrorResponse({ status: 409, error: { detail: "Randevu 'Planlandı' durumundayken çağrılamaz." } });
    expect(hataMesaji(hata)).toBe("Randevu 'Planlandı' durumundayken çağrılamaz.");
  });

  it('ValidationProblem için alan sözlüğündeki Türkçe mesajı verir', () => {
    const hata = new HttpErrorResponse({
      status: 400,
      error: { title: 'One or more validation errors occurred.', errors: { cevap: ['Bir seçenek seçin ya da kısaca yazın.'] } },
    });
    expect(hataMesaji(hata)).toBe('Bir seçenek seçin ya da kısaca yazın.');
  });

  it('detaysız 403 için yetki mesajı verir', () => {
    expect(hataMesaji(new HttpErrorResponse({ status: 403 }))).toContain('yetkiniz yok');
  });
});
