import { IkiAdimliOnay } from './iki-adimli-onay';

describe('IkiAdimliOnay', () => {
  afterEach(() => vi.useRealTimers());

  it('ilk tıklama onay bekler, ikinci tıklama çalıştırır', () => {
    const onay = new IkiAdimliOnay();
    expect(onay.onaylandi()).toBe(false);
    expect(onay.bekliyor()).toBe(true);
    expect(onay.onaylandi()).toBe(true);
    expect(onay.bekliyor()).toBe(false);
  });

  it('süre dolunca yeniden onay ister', () => {
    vi.useFakeTimers();
    const onay = new IkiAdimliOnay(4000);
    onay.onaylandi();
    vi.advanceTimersByTime(4000);
    expect(onay.bekliyor()).toBe(false);
    expect(onay.onaylandi()).toBe(false);
  });
});
