import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, input } from '@angular/core';
import { BransOgesi } from '../cekirdek/api';
import { SentetikRozeti } from '../ortak/sentetik-rozeti';
import { simdiSinyali } from '../ortak/simdi';
import { TrUpperPipe } from '../ortak/tr-upper.pipe';

/**
 * TV sıra ekranı (taslak 8): koyu, kontrolsüz, isimler maskeli, 64+ px. Bölüm adı trUpper ile
 * ("ENDOKRİNOLOJİ"). Çağrılar canlı olarak Aşama 7'de gelir.
 */
@Component({
  selector: 'app-tv',
  imports: [DatePipe, SentetikRozeti, TrUpperPipe],
  template: `
    <main class="flex min-h-dvh flex-col bg-slate-900 p-10 text-white">
      <header class="flex items-start justify-between gap-6">
        <h1 class="text-[64px] leading-tight font-semibold tracking-wide">
          @if (bransAd(); as ad) {
            {{ ad + ' polikliniği' | trUpper }}
          } @else if (branslar.error()) {
            Sıra ekranı
          } @else {
            Yükleniyor…
          }
        </h1>
        <div class="flex flex-col items-end gap-2">
          <p class="text-[64px] leading-tight font-semibold tabular-nums" aria-label="Saat">{{ simdi() | date: 'HH:mm' }}</p>
          <app-sentetik-rozeti [koyu]="true" />
        </div>
      </header>

      <section class="flex flex-1 flex-col items-center justify-center" aria-live="polite">
        @if (branslar.error()) {
          <p class="text-4xl text-slate-200"><span aria-hidden="true">○</span> Bağlantı bekleniyor…</p>
        } @else if (branslar.hasValue()) {
          <p class="text-[64px] font-semibold text-slate-200">Henüz çağrı yok</p>
        }
      </section>
    </main>
  `,
})
export class Tv {
  readonly bransId = input.required<string>();

  protected readonly branslar = httpResource<BransOgesi[]>(() => '/api/branslar');
  // value() hata durumunda istisna fırlatır; önce hasValue() ile korunur.
  protected readonly bransAd = computed(() =>
    this.branslar.hasValue() ? this.branslar.value().find((b) => b.id === this.bransId())?.ad : undefined,
  );
  protected readonly simdi = simdiSinyali(10_000);
}
