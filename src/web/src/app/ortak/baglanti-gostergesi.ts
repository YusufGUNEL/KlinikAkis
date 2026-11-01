import { Component, computed, input } from '@angular/core';
import { Ikon, IkonAdi } from './ikon';

export type BaglantiDurumu = 'canli' | 'baglaniyor' | 'koptu';

/** SignalR bağlantı göstergesi (SPEC §8): "● canlı / ○ koptu", metinli. Aşama 3'te HubService'e bağlanır. */
@Component({
  selector: 'app-baglanti-gostergesi',
  imports: [Ikon],
  template: `<span class="inline-flex items-center gap-1 font-medium" [class]="gorunum().sinif + (koyu() ? ' text-xl' : ' text-sm')" role="status"
    ><app-ikon [adi]="gorunum().ikon" />{{ gorunum().metin }}</span
  >`,
})
export class BaglantiGostergesi {
  readonly durum = input.required<BaglantiDurumu>();
  /** TV gibi koyu zeminde: açık tonlar (kontrast) ve büyük yazı. */
  readonly koyu = input(false);

  protected readonly gorunum = computed(
    () =>
      ({
        canli: { ikon: 'canli' as IkonAdi, metin: 'canlı', sinif: this.koyu() ? 'text-green-300' : 'text-basari' },
        baglaniyor: { ikon: 'baglaniyor' as IkonAdi, metin: 'bağlanıyor', sinif: this.koyu() ? 'text-slate-300' : 'text-slate-600' },
        koptu: { ikon: 'koptu' as IkonAdi, metin: 'koptu', sinif: this.koyu() ? 'text-amber-300' : 'text-uyari' },
      })[this.durum()],
  );
}
