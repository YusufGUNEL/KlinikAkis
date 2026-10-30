import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { HastaRandevusu } from '../cekirdek/api';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { Oturum } from '../cekirdek/oturum';
import { gunEtiketi } from '../cekirdek/randevu';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { DurumRozeti } from '../ortak/durum-rozeti';
import { simdiSinyali } from '../ortak/simdi';

/** Hasta ana sayfası: randevularım (canlı) + "Randevu al". Çağrılınca oda bilgisi bandı çıkar. */
@Component({
  selector: 'app-hasta-ana',
  imports: [BaglantiGostergesi, ButtonModule, DurumKutusu, DurumRozeti, RouterLink],
  template: `
    <div class="flex items-start justify-between gap-3">
      <h1 class="text-2xl font-semibold text-slate-900">Merhaba, {{ ad() }}</h1>
      <app-baglanti-gostergesi [durum]="hub.durum()" />
    </div>

    <a pButton routerLink="sikayet" class="mt-5 min-h-12 w-full justify-center text-lg">Randevu al</a>

    <section class="mt-6" aria-labelledby="randevular-baslik">
      <h2 id="randevular-baslik" class="text-lg font-semibold text-slate-900">Randevularım</h2>
      <div class="mt-3 flex flex-col gap-3">
        @if (randevular.error()) {
          <app-durum-kutusu tur="hata" (yenidenDene)="randevular.reload()" />
        } @else if (randevular.hasValue()) {
          @for (r of randevular.value(); track r.id) {
            <article class="rounded-lg border border-slate-200 p-4" [attr.aria-label]="r.bransAd + ' randevusu'">
              @if (r.durum === 'Cagrildi') {
                <p class="-mx-4 -mt-4 mb-3 rounded-t-lg bg-bilgi px-4 py-3 font-semibold text-white" role="alert">
                  <span aria-hidden="true">→</span> Sıranız geldi. Lütfen geçiniz: Oda {{ r.oda }}
                </p>
              }
              <p class="flex items-center justify-between gap-2">
                <span class="font-semibold tabular-nums text-slate-900">{{ etiket(r.baslangic) }}</span>
                <app-durum-rozeti [durum]="r.durum" />
              </p>
              <p class="mt-1 text-slate-900">{{ r.bransAd }}</p>
              <p class="text-[17px] text-slate-700">{{ r.hekimAd }} · Oda {{ r.oda }}</p>
              <p class="mt-1 text-[17px] text-slate-700">Sıra no: <span class="font-semibold tabular-nums">{{ r.siraNo }}</span></p>
            </article>
          } @empty {
            <app-durum-kutusu tur="bos" mesaj="Henüz randevunuz yok." />
          }
        } @else {
          <app-durum-kutusu tur="yukleniyor" />
        }
      </div>
    </section>
  `,
})
export class HastaAna {
  private readonly oturum = inject(Oturum);
  protected readonly hub = inject(HubService);

  // Ekran gece yarısını geçerek açık kalırsa "Bugün/Yarın" etiketi kendiliğinden güncellenir.
  private readonly simdi = simdiSinyali();
  protected readonly ad = computed(() => this.oturum.aktif()?.ad ?? '');
  protected readonly randevular = httpResource<HastaRandevusu[]>(() => '/api/hasta/me/randevular');

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.randevular);
  }

  protected etiket(iso: string): string {
    return gunEtiketi(iso, this.simdi());
  }
}
