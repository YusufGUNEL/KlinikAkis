import { Component, computed, input } from '@angular/core';
import { Isaret, sayi } from '../cekirdek/tahlil';

/**
 * Referans aralık çubuğu (taslak 3): gri ölçek, yeşil normal bant, değer noktası. Nokta rengi işaretten gelir;
 * aynı bilgi altında metin olarak da yazar (renk tek ipucu değil).
 */
@Component({
  selector: 'app-aralik-cubugu',
  template: `
    <div class="mt-2" data-test="aralik-cubugu">
      <div class="relative h-3 rounded-full bg-slate-200" aria-hidden="true">
        <div class="absolute inset-y-0 rounded-full bg-basari/30" [style.left.%]="konum().bantSol" [style.width.%]="konum().bantGenislik"></div>
        <div
          class="absolute top-1/2 size-4 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white"
          [class]="noktaRengi[isaret()]"
          [style.left.%]="konum().nokta"
        ></div>
      </div>
      <p class="mt-1 text-base text-slate-700 tabular-nums">Referans aralığı: {{ altMetin() }}–{{ ustMetin() }} {{ birim() }}</p>
    </div>
  `,
})
export class AralikCubugu {
  readonly deger = input.required<number>();
  readonly alt = input.required<number>();
  readonly ust = input.required<number>();
  readonly ondalik = input.required<number>();
  readonly birim = input.required<string>();
  readonly isaret = input.required<Isaret>();

  protected readonly altMetin = computed(() => sayi(this.alt(), this.ondalik()));
  protected readonly ustMetin = computed(() => sayi(this.ust(), this.ondalik()));

  /** Ölçek: aralığın yarısı kadar iki yana pay; değer dışarıdaysa ölçek onu da kapsar. */
  protected readonly konum = computed(() => {
    const [a, u, d] = [this.alt(), this.ust(), this.deger()];
    const pay = (u - a) / 2 || 1;
    const min = Math.min(a - pay, d - pay / 4);
    const max = Math.max(u + pay, d + pay / 4);
    const yuzde = (v: number) => ((v - min) / (max - min)) * 100;
    return { bantSol: yuzde(a), bantGenislik: yuzde(u) - yuzde(a), nokta: yuzde(d) };
  });

  // Tailwind sınıfları kaynakta tam adıyla geçmeli (çalışma anında birleştirilen ad derlemeye girmez).
  protected readonly noktaRengi: Record<Isaret, string> = {
    Normal: 'bg-basari',
    Yuksek: 'bg-uyari',
    Dusuk: 'bg-uyari',
    KritikYuksek: 'bg-tehlike',
    KritikDusuk: 'bg-tehlike',
  };
}
