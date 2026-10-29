import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from './app.config';
import { GirisYaniti } from './cekirdek/api';

function oturumKoy(oturum: Partial<GirisYaniti>): void {
  sessionStorage.setItem(
    'klinikakis.oturum',
    JSON.stringify({ token: 't', ad: 'X', kimlikId: null, bitis: '2999-01-01T00:00:00Z', ...oturum }),
  );
}

describe('Rotalar ve rol guard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
  });

  it('kök adres girişe yönlenir', async () => {
    const harness = await RouterTestingHarness.create('/');
    expect(TestBed.inject(Router).url).toBe('/giris');
    expect(harness.routeNativeElement?.textContent).toContain('Demo verisini sıfırla');
  });

  it('oturumsuz doktor ekranı girişe yönlenir', async () => {
    await RouterTestingHarness.create('/doktor');
    expect(TestBed.inject(Router).url).toBe('/giris');
  });

  it('hasta oturumu doktor ekranını açamaz', async () => {
    oturumKoy({ rol: 'hasta', kimlikId: 'a1' });
    await RouterTestingHarness.create('/doktor');
    expect(TestBed.inject(Router).url).toBe('/giris');
  });

  it('doktor oturumu doktor ekranını açar', async () => {
    oturumKoy({ rol: 'doktor', kimlikId: 'b1' });
    await RouterTestingHarness.create('/doktor');
    expect(TestBed.inject(Router).url).toBe('/doktor');
  });

  it("TV yalnızca token'daki bölümü açar", async () => {
    oturumKoy({ rol: 'tv', kimlikId: 'brans-1' });
    const harness = await RouterTestingHarness.create('/tv/brans-2');
    expect(TestBed.inject(Router).url).toBe('/giris');
    await harness.navigateByUrl('/tv/brans-1');
    expect(TestBed.inject(Router).url).toBe('/tv/brans-1');
  });

  it('süresi dolmuş oturum yok sayılır', async () => {
    oturumKoy({ rol: 'doktor', bitis: '2000-01-01T00:00:00Z' });
    await RouterTestingHarness.create('/doktor');
    expect(TestBed.inject(Router).url).toBe('/giris');
  });
});
