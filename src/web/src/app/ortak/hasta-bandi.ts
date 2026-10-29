import { Component, computed, input } from '@angular/core';
import { HastaOzeti } from '../cekirdek/api';
import { SentetikRozeti } from './sentetik-rozeti';

/**
 * Doktor ekranının sabit hasta bandı (taslak 4). Alerji her zaman görünür: doluysa kırmızı + ikon + metin,
 * boşsa nötr "Bilinen alerji yok". Kırmızı yalnızca burada, alerji için.
 */
@Component({
  selector: 'app-hasta-bandi',
  imports: [SentetikRozeti],
  template: `
    @let h = ozet();
    <header class="border-b border-slate-200 bg-white px-4 py-3" aria-label="Hasta bilgisi">
      <p class="flex flex-wrap items-center gap-x-2 text-base font-semibold text-slate-900">
        <span>{{ h.ad }} {{ h.soyad.charAt(0) }}.</span>
        <span class="text-slate-400" aria-hidden="true">·</span>
        <span class="tabular-nums">{{ h.yas }} {{ h.cinsiyet }}</span>
        <span class="text-slate-400" aria-hidden="true">·</span>
        <span class="font-normal tabular-nums text-slate-700">TC {{ h.tcMaskeli }}</span>
        @if (h.sentetik) {
          <app-sentetik-rozeti />
        }
      </p>
      <p class="mt-1 flex flex-wrap items-center gap-x-2 text-sm">
        @if (alerjiVar()) {
          <span class="font-semibold text-tehlike" data-test="alerji">
            <span aria-hidden="true">⛔</span> Alerji: {{ h.alerjiler.join(', ') }}
          </span>
        } @else {
          <span class="text-slate-700" data-test="alerji">Alerji: Bilinen alerji yok</span>
        }
        @for (hastalik of h.kronikHastaliklar; track hastalik) {
          <span class="text-slate-400" aria-hidden="true">·</span>
          <span class="text-slate-700">{{ hastalik }}</span>
        }
        <span class="text-slate-400" aria-hidden="true">·</span>
        <span class="text-slate-700" [title]="h.kullandigiIlaclar.join(', ')">{{ ilacMetni() }}</span>
      </p>
    </header>
  `,
})
export class HastaBandi {
  readonly ozet = input.required<HastaOzeti>();

  protected readonly alerjiVar = computed(() => this.ozet().alerjiler.length > 0);
  protected readonly ilacMetni = computed(() => {
    const sayi = this.ozet().kullandigiIlaclar.length;
    return sayi === 0 ? 'Kullandığı ilaç yok' : `${sayi} ilaç`;
  });
}
