import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { Oturum } from '../cekirdek/oturum';
import { SentetikRozeti } from '../ortak/sentetik-rozeti';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { Ikon } from '../ortak/ikon';
import { HastaOzeti } from '../cekirdek/api';
import { httpResource } from '@angular/common/http';

/** Mobil kabuk (390 px hedef): gövde 18 px, dokunma hedefleri ≥ 48 px (SPEC §10.1). */
@Component({
  selector: 'app-hasta-kabugu',
  imports: [DurumKutusu, Ikon, RouterOutlet, SentetikRozeti],
  template: `
    <div class="mx-auto flex min-h-dvh max-w-md flex-col bg-white text-[18px] leading-relaxed">
      <header class="flex items-center justify-between gap-2 border-b border-slate-200 px-4 py-2">
        <span class="font-semibold text-ana">KlinikAkış</span>
        <span class="flex items-center gap-2">
          <app-sentetik-rozeti />
          <button
            type="button"
            class="min-h-12 rounded-lg px-3 text-base font-medium text-slate-700 hover:bg-slate-100"
            (click)="cikis()"
          >
            Çıkış
          </button>
        </span>
      </header>
      @if (ozet.hasValue()) {
        <p class="mx-4 mt-3 rounded-lg px-3 py-2 text-base" [class]="ozet.value()!.alerjiler.length ? 'bg-red-50 text-tehlike' : 'bg-slate-100 text-slate-700'" data-test="hasta-alerji">
          <app-ikon [adi]="ozet.value()!.alerjiler.length ? 'uyari' : 'bos'" />
          Alerji: {{ ozet.value()!.alerjiler.length ? ozet.value()!.alerjiler.join(', ') : 'Bilinen alerji yok' }}
        </p>
      } @else if (ozet.error()) {
        <p class="mx-4 mt-3 text-sm text-uyari" role="alert">Alerji bilgisi alınamadı.</p>
      } @else {
        <div class="mx-4 mt-3"><app-durum-kutusu tur="yukleniyor" mesaj="Alerji bilgisi yükleniyor…" /></div>
      }
      <main class="flex-1 px-4 py-5">
        <router-outlet />
      </main>
    </div>
  `,
})
export class HastaKabugu {
  private readonly oturum = inject(Oturum);
  private readonly router = inject(Router);
  protected readonly ozet = httpResource<HastaOzeti>(() => '/api/hasta/me/ozet');

  protected cikis(): void {
    this.oturum.cikis();
    void this.router.navigate(['/giris']);
  }
}
