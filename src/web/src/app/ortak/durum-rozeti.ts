import { Component, computed, input } from '@angular/core';
import { RandevuDurumu } from '../cekirdek/api';

// Randevu durumu → ikon + metin + renk (renk tek ipucu değil). Kırmızı kullanılmaz: o yalnızca
// alerji, kritik değer ve kırmızı bayrak içindir.
const ETIKETLER: Record<RandevuDurumu, { ikon: string; metin: string; sinif: string }> = {
  Planlandi: { ikon: '○', metin: 'Planlandı', sinif: 'text-slate-700' },
  KabulEdildi: { ikon: '●', metin: 'Kabul edildi', sinif: 'text-bilgi' },
  Cagrildi: { ikon: '→', metin: 'Çağrıldı', sinif: 'text-ana' },
  Muayenede: { ikon: '▶', metin: 'Muayenede', sinif: 'text-ana' },
  Tamamlandi: { ikon: '✓', metin: 'Tamamlandı', sinif: 'text-basari' },
  Iptal: { ikon: '⊘', metin: 'İptal', sinif: 'text-slate-600' },
  Gelmedi: { ikon: '✗', metin: 'Gelmedi', sinif: 'text-slate-600' },
};

@Component({
  selector: 'app-durum-rozeti',
  template: `<span class="inline-flex items-center gap-1 whitespace-nowrap font-medium" [class]="etiket().sinif"
    ><span aria-hidden="true">{{ etiket().ikon }}</span>{{ etiket().metin }}</span
  >`,
})
export class DurumRozeti {
  readonly durum = input.required<RandevuDurumu>();

  protected readonly etiket = computed(() => ETIKETLER[this.durum()]);
}
