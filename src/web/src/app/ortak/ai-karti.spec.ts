import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { AiKaynak } from '../cekirdek/api';
import { AiKarti, HekimOnayi } from './ai-karti';

@Component({
  imports: [AiKarti],
  template: `<app-ai-karti [kaynak]="kaynak()" zaman="2026-10-29T10:52:00Z" model="claude-haiku" [onay]="onay()">
    <p>Birkaç haftadır susama</p>
    <button aiButonlar type="button">Onayla</button>
  </app-ai-karti>`,
})
class Kap {
  readonly kaynak = signal<AiKaynak>('Llm');
  readonly onay = signal<HekimOnayi | null>(null);
}

function kur() {
  TestBed.configureTestingModule({ providers: appConfig.providers });
  const fixture = TestBed.createComponent(Kap);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, kart: () => el.querySelector('[data-test="ai-karti"]')! };
}

describe('AiKarti', () => {
  it('taslak: mor kesik kenar, model ve İstanbul saatiyle başlık, butonlar görünür', () => {
    const { kart, el } = kur();
    const baslik = el.querySelector('[data-test="ai-baslik"]')!.textContent;
    expect(kart().classList).toContain('border-dashed');
    expect(kart().classList).toContain('border-ai');
    expect(baslik).toContain('AI taslağı · klinik karar hekime aittir · claude-haiku');
    expect(baslik).toContain('13:52'); // 10:52 UTC → İstanbul
    expect(el.textContent).toContain('Onayla');
  });

  it('yedek kaynakta kural tabanlı başlık, yine mor kesik', async () => {
    const { fixture, kart, el } = kur();
    fixture.componentInstance.kaynak.set('Yedek');
    await fixture.whenStable();
    expect(el.textContent).toContain('Otomatik taslak · model yanıt vermedi, kural tabanlı');
    expect(kart().classList).toContain('border-dashed');
  });

  it('onaylanınca kenar düz olur, "onayladı" yazar, butonlar kalkar', async () => {
    const { fixture, kart, el } = kur();
    fixture.componentInstance.onay.set({ hekimAd: 'Uzm. Dr. A. Yılmaz', zaman: '2026-10-29T11:32:00Z' });
    await fixture.whenStable();
    const onay = el.querySelector('[data-test="ai-onay"]')!.textContent;
    expect(kart().classList).toContain('border-solid');
    expect(kart().classList).not.toContain('border-dashed');
    expect(onay).toContain('Uzm. Dr. A. Yılmaz onayladı');
    expect(onay).toContain('14:32');
    expect(el.textContent).not.toContain('Onayla');
  });
});
