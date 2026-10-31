import { Component, computed, input } from '@angular/core';

export type BaglantiDurumu = 'canli' | 'baglaniyor' | 'koptu';

/** SignalR bağlantı göstergesi (SPEC §8): "● canlı / ○ koptu", metinli. Aşama 3'te HubService'e bağlanır. */
@Component({
  selector: 'app-baglanti-gostergesi',
  template: `<span class="inline-flex items-center gap-1 font-medium" [class]="gorunum().sinif + (koyu() ? ' text-xl' : ' text-sm')" role="status"
    ><span aria-hidden="true">{{ gorunum().ikon }}</span>{{ gorunum().metin }}</span
  >`,
})
export class BaglantiGostergesi {
  readonly durum = input.required<BaglantiDurumu>();
  /** TV gibi koyu zeminde: açık tonlar (kontrast) ve büyük yazı. */
  readonly koyu = input(false);

  protected readonly gorunum = computed(
    () =>
      ({
        canli: { ikon: '●', metin: 'canlı', sinif: this.koyu() ? 'text-green-300' : 'text-basari' },
        baglaniyor: { ikon: '◌', metin: 'bağlanıyor', sinif: this.koyu() ? 'text-slate-300' : 'text-slate-600' },
        koptu: { ikon: '○', metin: 'koptu', sinif: this.koyu() ? 'text-amber-300' : 'text-uyari' },
      })[this.durum()],
  );
}
