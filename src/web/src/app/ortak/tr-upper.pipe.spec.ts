import { TrUpperPipe } from './tr-upper.pipe';

describe('TrUpperPipe', () => {
  const pipe = new TrUpperPipe();

  it('Türkçe i/ı harflerini doğru büyütür', () => {
    expect(pipe.transform('endokrinoloji polikliniği')).toBe('ENDOKRİNOLOJİ POLİKLİNİĞİ');
    expect(pipe.transform('ılık şiş')).toBe('ILIK ŞİŞ');
    expect(pipe.transform('İç Hastalıkları')).toBe('İÇ HASTALIKLARI');
    expect(pipe.transform('SENTETİK')).toBe('SENTETİK');
    // Karşılaştırma: yerel ayarsız toUpperCase "I" verir; projede bu yüzden kullanılmaz.
    expect('i'.toUpperCase()).toBe('I');
  });

  it('boş değerde boş metin döner', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });
});
