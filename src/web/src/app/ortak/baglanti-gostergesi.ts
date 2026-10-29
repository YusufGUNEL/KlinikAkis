import { Component, computed, input } from '@angular/core';

export type BaglantiDurumu = 'canli' | 'baglaniyor' | 'koptu';

/** SignalR bağlantı göstergesi (SPEC §8): "● canlı / ○ koptu", metinli. Aşama 3'te HubService'e bağlanır. */
@Component({
  selector: 'app-baglanti-gostergesi',
  template: `<span class="inline-flex items-center gap-1 text-sm font-medium" [class]="gorunum().sinif" role="status"
    ><span aria-hidden="true">{{ gorunum().ikon }}</span>{{ gorunum().metin }}</span
  >`,
})
export class BaglantiGostergesi {
  readonly durum = input.required<BaglantiDurumu>();

  protected readonly gorunum = computed(
    () =>
      ({
        canli: { ikon: '●', metin: 'canlı', sinif: 'text-basari' },
        baglaniyor: { ikon: '◌', metin: 'bağlanıyor', sinif: 'text-slate-600' },
        koptu: { ikon: '○', metin: 'koptu', sinif: 'text-uyari' },
      })[this.durum()],
  );
}
