import { Component, computed, input } from '@angular/core';
import { RandevuDurumu } from '../cekirdek/api';
import { Ikon, IkonAdi } from './ikon';

// Randevu durumu → ikon + metin + renk (renk tek ipucu değil). Kırmızı kullanılmaz: o yalnızca
// alerji, kritik değer ve kırmızı bayrak içindir.
const ETIKETLER: Record<RandevuDurumu, { ikon: IkonAdi; metin: string; sinif: string }> = {
  Planlandi: { ikon: 'planlandi', metin: 'Planlandı', sinif: 'text-slate-700' },
  KabulEdildi: { ikon: 'kabul', metin: 'Kabul edildi', sinif: 'text-bilgi' },
  Cagrildi: { ikon: 'cagrildi', metin: 'Çağrıldı', sinif: 'text-ana' },
  Muayenede: { ikon: 'muayenede', metin: 'Muayenede', sinif: 'text-ana' },
  Tamamlandi: { ikon: 'tamamlandi', metin: 'Tamamlandı', sinif: 'text-basari' },
  Iptal: { ikon: 'iptal', metin: 'İptal', sinif: 'text-slate-600' },
  Gelmedi: { ikon: 'gelmedi', metin: 'Gelmedi', sinif: 'text-slate-600' },
};

@Component({
  selector: 'app-durum-rozeti',
  imports: [Ikon],
  template: `<span class="inline-flex items-center gap-1 whitespace-nowrap font-medium" [class]="etiket().sinif"
    ><app-ikon [adi]="etiket().ikon" />{{ etiket().metin }}</span
  >`,
})
export class DurumRozeti {
  readonly durum = input.required<RandevuDurumu>();

  protected readonly etiket = computed(() => ETIKETLER[this.durum()]);
}
