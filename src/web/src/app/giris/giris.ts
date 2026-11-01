import { httpResource } from '@angular/common/http';
import { Component, inject, input, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { KimlikOgesi, Rol, ROLLER } from '../cekirdek/api';
import { Demo } from '../cekirdek/demo';
import { Oturum, rolAnaSayfasi } from '../cekirdek/oturum';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { IkiAdimliOnay } from '../ortak/iki-adimli-onay';
import { SentetikRozeti } from '../ortak/sentetik-rozeti';

interface RolKarti {
  rol: Rol;
  baslik: string;
  aciklama: string;
  ikon: string;
}

const KARTLAR: RolKarti[] = [
  { rol: 'hasta', baslik: 'Hasta', aciklama: 'Telefon ekranı', ikon: '📱' },
  { rol: 'doktor', baslik: 'Doktor', aciklama: 'Poliklinik çalışma alanı', ikon: '🩺' },
  { rol: 'kabul', baslik: 'Kabul', aciklama: 'Hasta kabul masası', ikon: '🗂️' },
  { rol: 'tv', baslik: 'TV', aciklama: 'Bekleme salonu sıra ekranı', ikon: '📺' },
];

const LISTE_BASLIGI: Partial<Record<Rol, string>> = {
  hasta: 'Senaryo hastası seçin',
  doktor: 'Doktor seçin',
  tv: 'Bölüm seçin',
};

/**
 * Rol seçici (taslak §10.4). Derin bağlantı: /giris?rol=hasta&kimlik=<Id> otomatik giriş yapar
 * (doktor ekranındaki QR bu adresi açar). Kabul için kimlik gerekmez: /giris?rol=kabul.
 */
@Component({
  selector: 'app-giris',
  imports: [ButtonModule, DurumKutusu, SentetikRozeti],
  template: `
    <main class="mx-auto max-w-3xl px-4 py-8">
      <header class="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 class="text-2xl font-semibold text-slate-900">KlinikAkış</h1>
          <p class="text-slate-700">Poliklinik akışı demosu · rol seçin</p>
        </div>
        <app-sentetik-rozeti />
      </header>

      <ul class="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2" aria-label="Roller">
        @for (kart of kartlar; track kart.rol) {
          <li>
            <button
              type="button"
              class="flex min-h-28 w-full items-center gap-4 rounded-xl border-2 bg-white p-5 text-left transition hover:border-ana"
              [class.border-ana]="secilenRol() === kart.rol"
              [class.border-slate-200]="secilenRol() !== kart.rol"
              [attr.aria-pressed]="secilenRol() === kart.rol"
              [disabled]="girisSuruyor()"
              (click)="rolSec(kart.rol)"
            >
              <span class="text-4xl" aria-hidden="true">{{ kart.ikon }}</span>
              <span>
                <span class="block text-xl font-semibold text-slate-900">
                  {{ kart.baslik }}
                  @if (secilenRol() === kart.rol) {
                    <span class="text-base font-medium text-ana">✓ seçildi</span>
                  }
                </span>
                <span class="block text-slate-700">{{ kart.aciklama }}</span>
              </span>
            </button>
          </li>
        }
      </ul>

      @if (listeBasligi(); as baslik) {
        <section class="mt-6" aria-live="polite">
          <h2 class="text-lg font-semibold text-slate-900">{{ baslik }}</h2>
          <div class="mt-3">
            @if (kimlikler.isLoading()) {
              <app-durum-kutusu tur="yukleniyor" />
            } @else if (kimlikler.error()) {
              <app-durum-kutusu tur="hata" mesaj="Liste alınamadı. Sunucunun çalıştığını kontrol edin." (yenidenDene)="kimlikler.reload()" />
            } @else if (kimlikler.value()?.length === 0) {
              <app-durum-kutusu tur="bos" mesaj="Bu rol için seçilecek kayıt yok. Demo verisini sıfırlamayı deneyin." />
            } @else {
              <ul class="grid grid-cols-1 gap-2 sm:grid-cols-2">
                @for (k of kimlikler.value(); track k.id) {
                  <li>
                    <button
                      type="button"
                      class="flex min-h-12 w-full flex-col justify-center rounded-lg border border-slate-200 bg-white px-4 py-2 text-left hover:border-ana"
                      [disabled]="girisSuruyor()"
                      (click)="girisYap(secilenRol()!, k.id)"
                    >
                      <span class="font-medium text-slate-900">{{ k.ad }}</span>
                      @if (k.aciklama) {
                        <span class="text-sm text-slate-700">{{ k.aciklama }}</span>
                      }
                    </button>
                  </li>
                }
              </ul>
            }
          </div>
        </section>
      }

      @if (hata()) {
        <p class="mt-4 rounded-lg border border-slate-300 bg-white p-3 text-slate-900" role="alert">
          <span aria-hidden="true">✗</span> {{ hata() }}
        </p>
      }

      <footer class="mt-10 flex flex-wrap items-center gap-3 border-t border-slate-200 pt-4">
        <button pButton type="button" severity="secondary" variant="outlined" class="ikincil min-h-12" [class.onay-bekliyor]="sifirlaOnayi.bekliyor()" [disabled]="sifirlaniyor()" (click)="sifirla()">
          {{ sifirlaniyor() ? 'Sıfırlanıyor…' : sifirlaOnayi.bekliyor() ? 'Emin misiniz? Sıfırla' : 'Demo verisini sıfırla' }}
        </button>
        <span class="sr-only" aria-live="polite">{{ sifirlaOnayi.bekliyor() ? sifirlaOnayi.duyuru : '' }}</span>
        @if (sifirlamaMesaji()) {
          <span class="text-slate-700" role="status">{{ sifirlamaMesaji() }}</span>
        }
      </footer>
    </main>
  `,
})
export class Giris implements OnInit {
  // Derin bağlantı sorgu parametreleri (withComponentInputBinding).
  readonly rol = input<string>();
  readonly kimlik = input<string>();

  private readonly oturum = inject(Oturum);
  private readonly demo = inject(Demo);
  private readonly router = inject(Router);

  protected readonly kartlar = KARTLAR;
  protected readonly secilenRol = signal<Rol | null>(null);
  protected readonly girisSuruyor = signal(false);
  protected readonly hata = signal('');
  protected readonly sifirlaniyor = signal(false);
  protected readonly sifirlaOnayi = new IkiAdimliOnay();
  protected readonly sifirlamaMesaji = signal('');

  protected readonly kimlikler = httpResource<KimlikOgesi[]>(() => {
    const rol = this.secilenRol();
    return rol && rol !== 'kabul' ? `/api/demo/kimlikler?rol=${rol}` : undefined;
  });

  protected listeBasligi(): string | undefined {
    const rol = this.secilenRol();
    return rol ? LISTE_BASLIGI[rol] : undefined;
  }

  ngOnInit(): void {
    const rol = this.rol() as Rol | undefined;
    if (!rol || !ROLLER.includes(rol)) return;
    if (rol === 'kabul') void this.girisYap(rol, null);
    else if (this.kimlik()) void this.girisYap(rol, this.kimlik()!);
    else this.secilenRol.set(rol);
  }

  protected rolSec(rol: Rol): void {
    this.hata.set('');
    if (rol === 'kabul') void this.girisYap(rol, null);
    else this.secilenRol.set(rol);
  }

  protected async girisYap(rol: Rol, kimlikId: string | null): Promise<void> {
    this.girisSuruyor.set(true);
    this.hata.set('');
    try {
      const oturum = await this.oturum.girisYap(rol, kimlikId);
      await this.router.navigate(rolAnaSayfasi(oturum));
    } catch {
      this.hata.set('Giriş yapılamadı. Adresteki kimlik bulunamadı; listeden seçin.');
      this.secilenRol.set(rol === 'kabul' ? null : rol);
    } finally {
      this.girisSuruyor.set(false);
    }
  }

  protected async sifirla(): Promise<void> {
    if (!this.sifirlaOnayi.onaylandi()) return;
    this.sifirlaniyor.set(true);
    this.sifirlamaMesaji.set('');
    try {
      const s = await this.demo.sifirla();
      this.sifirlamaMesaji.set(`✓ Demo verisi sıfırlandı · ${s.hasta} hasta, ${s.slot} randevu saati`);
      this.kimlikler.reload();
    } catch {
      this.sifirlamaMesaji.set('✗ Sıfırlanamadı. Sunucunun çalıştığını kontrol edin.');
    } finally {
      this.sifirlaniyor.set(false);
    }
  }
}
