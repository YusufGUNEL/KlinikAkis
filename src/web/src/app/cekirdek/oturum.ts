import { HttpClient } from '@angular/common/http';
import { computed, inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GirisYaniti, Rol } from './api';

// sessionStorage: oturum sekmeye özgü. Aynı dizüstünde doktor ve kabul sekmeleri birbirinin
// token'ını ezmez; derin bağlantı (QR) yeni sekmede kendi oturumunu açar.
const ANAHTAR = 'klinikakis.oturum';

function okunanOturum(): GirisYaniti | null {
  try {
    const ham = sessionStorage.getItem(ANAHTAR);
    if (!ham) return null;
    const oturum = JSON.parse(ham) as GirisYaniti;
    return Date.parse(oturum.bitis) > Date.now() ? oturum : null;
  } catch {
    return null;
  }
}

/** Demo JWT oturumu (SPEC §7). Token'daki rol/kimlik sunucuda imzalıdır; burada yalnızca yönlendirme için okunur. */
@Service()
export class Oturum {
  private readonly http = inject(HttpClient);
  private readonly aktifOturum = signal<GirisYaniti | null>(okunanOturum());

  readonly aktif = this.aktifOturum.asReadonly();
  readonly rol = computed(() => this.aktifOturum()?.rol ?? null);
  readonly token = computed(() => this.aktifOturum()?.token ?? null);

  async girisYap(rol: Rol, kimlikId: string | null): Promise<GirisYaniti> {
    const yanit = await firstValueFrom(this.http.post<GirisYaniti>('/api/demo/login', { rol, kimlikId }));
    this.aktifOturum.set(yanit);
    try {
      sessionStorage.setItem(ANAHTAR, JSON.stringify(yanit));
    } catch {
      // Gizli pencerede depolama kapalı olabilir; oturum sekme açık kaldıkça bellekte yaşar.
    }
    return yanit;
  }

  cikis(): void {
    this.aktifOturum.set(null);
    try {
      sessionStorage.removeItem(ANAHTAR);
    } catch {
      // yok sayılır
    }
  }
}

/** Girişten sonra rolün ana ekranı. */
export function rolAnaSayfasi(oturum: Pick<GirisYaniti, 'rol' | 'kimlikId'>): string[] {
  return oturum.rol === 'tv' ? ['/tv', oturum.kimlikId ?? ''] : ['/' + oturum.rol];
}
