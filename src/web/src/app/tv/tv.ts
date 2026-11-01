import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { BransOgesi, TvCagrisi } from '../cekirdek/api';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { SentetikRozeti } from '../ortak/sentetik-rozeti';
import { simdiSinyali } from '../ortak/simdi';
import { TrUpperPipe } from '../ortak/tr-upper.pipe';

/**
 * TV sıra ekranı (taslak 8): koyu, kontrolsüz, isimler maskeli, 64+ px. Bölüm adı trUpper ile
 * ("ENDOKRİNOLOJİ"). Son 3 çağrı sunucudan (branş token'dan, ad baş harfe indirgenmiş); "Hastayı çağır"
 * HastaCagrildi olayıyla canlı gelir. Yeni çağrı kısa bir vurguyla belirir (hareket azaltma tercihine saygılı).
 */
@Component({
  selector: 'app-tv',
  imports: [BaglantiGostergesi, DatePipe, SentetikRozeti, TrUpperPipe],
  styles: `
    @media (prefers-reduced-motion: no-preference) {
      .vurgu {
        animation: vurgu 1.2s ease-out 2;
      }
    }
    @keyframes vurgu {
      0% {
        background-color: rgb(255 255 255 / 0.25);
      }
      100% {
        background-color: transparent;
      }
    }
  `,
  template: `
    <main class="flex min-h-dvh flex-col bg-slate-900 p-10 text-white">
      <header class="flex items-start justify-between gap-6">
        <h1 class="max-w-[62%] text-center text-4xl leading-tight font-semibold tracking-wide sm:text-5xl lg:text-[64px]">
          @if (bransAd(); as ad) {
            {{ ad + ' polikliniği' | trUpper }}
          } @else if (branslar.error()) {
            Sıra ekranı
          } @else {
            Yükleniyor…
          }
        </h1>
        <div class="flex flex-col items-end gap-2">
          <p class="text-[64px] leading-tight font-semibold tabular-nums"><span class="sr-only">Saat </span>{{ simdi() | date: 'HH:mm' }}</p>
          <div class="flex items-center gap-4">
            <app-baglanti-gostergesi [durum]="hub.durum()" [koyu]="true" />
            <app-sentetik-rozeti [koyu]="true" />
          </div>
        </div>
      </header>

      <section class="flex flex-1 flex-col items-center justify-center gap-10" aria-live="polite">
        @if (cagrilar.hasValue() && cagrilar.value().length) {
          @let liste = cagrilar.value();
          <!-- Anahtar çağrı anı: yeni çağrıda öğe yeniden kurulur ve vurgu oynar; ekran açılışındaki çağrıda oynamaz. -->
          @for (c of [liste[0]]; track anahtar(c)) {
            <div class="flex flex-col items-center rounded-2xl px-12 py-6" [class.vurgu]="anahtar(c) !== ilkAnahtar()" data-test="son-cagri">
              <p class="text-[144px] leading-none font-bold tabular-nums">
                {{ c.siraNo }} <span aria-hidden="true">→</span><span class="sr-only">,</span> ODA {{ c.oda }}
              </p>
              <p class="mt-4 text-[64px] text-slate-200">({{ c.basHarfler }})</p>
            </div>
          }
          @if (liste.length > 1) {
            <div class="flex flex-wrap items-baseline justify-center gap-x-12 gap-y-2 text-[48px] text-slate-300" data-test="onceki-cagrilar">
              <h2 class="font-semibold">Son çağrılar:</h2>
              @for (c of liste.slice(1); track anahtar(c)) {
                <p class="tabular-nums">{{ c.siraNo }} <span aria-hidden="true">→</span><span class="sr-only">, oda</span> {{ c.oda }}</p>
              }
            </div>
          }
        } @else if (cagrilar.error() || branslar.error()) {
          <p class="text-[64px] font-semibold text-slate-200"><span aria-hidden="true">○</span> Bağlantı bekleniyor…</p>
        } @else if (cagrilar.hasValue()) {
          <p class="text-[64px] font-semibold text-slate-200">Henüz çağrı yok</p>
        } @else {
          <p class="text-[64px] font-semibold text-slate-300">Yükleniyor…</p>
        }
      </section>
    </main>
  `,
})
export class Tv {
  protected readonly hub = inject(HubService);
  readonly bransId = input.required<string>();

  protected readonly branslar = httpResource<BransOgesi[]>(() => '/api/branslar');
  protected readonly cagrilar = httpResource<TvCagrisi[]>(() => '/api/tv/cagrilar');
  // value() hata durumunda istisna fırlatır; önce hasValue() ile korunur.
  protected readonly bransAd = computed(() =>
    this.branslar.hasValue() ? this.branslar.value().find((b) => b.id === this.bransId())?.ad : undefined,
  );
  protected readonly simdi = simdiSinyali(10_000);

  protected readonly anahtar = (c: TvCagrisi) => c.siraNo + c.zaman;
  private ilk: string | undefined;
  /** Ekran açıldığında zaten gösterilen çağrı: ilk yüklemede bir kez sabitlenir, vurgu yalnızca sonraki çağrılarda oynar. */
  protected readonly ilkAnahtar = computed(() => {
    if (this.ilk === undefined && this.cagrilar.hasValue()) {
      const liste = this.cagrilar.value();
      this.ilk = liste.length ? this.anahtar(liste[0]) : '';
    }
    return this.ilk;
  });

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.cagrilar);
  }
}
