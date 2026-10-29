import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { Tv } from './tv';

function kur() {
  TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Tv);
  fixture.componentRef.setInput('bransId', 'endo');
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

describe('Tv', () => {
  it('bölüm adını Türkçe büyük harfle gösterir', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').flush([{ id: 'endo', kod: 'Endokrinoloji', ad: 'Endokrinoloji', hekimSayisi: 1 }]);
    await fixture.whenStable();
    expect(el.querySelector('h1')!.textContent).toContain('ENDOKRİNOLOJİ POLİKLİNİĞİ');
    expect(el.textContent).toContain('Henüz çağrı yok');
  });

  // Hata durumunda httpResource.value() istisna fırlatır; ekran "Yükleniyor…"da takılmamalı.
  it('bölüm listesi alınamazsa hata durumunu gösterir', async () => {
    const { fixture, http, el } = kur();
    http.expectOne('/api/branslar').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(el.textContent).toContain('Bağlantı bekleniyor');
    expect(el.textContent).not.toContain('Yükleniyor');
    expect(el.textContent).not.toContain('Henüz çağrı yok');
  });
});
