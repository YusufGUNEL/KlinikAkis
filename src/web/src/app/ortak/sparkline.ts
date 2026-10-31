import { Component, computed, input } from '@angular/core';

/**
 * Küçük trend çizgisi (elle SVG; grafik paketi yok). Görsel yalnızca destek: değerler `etiket` ile metin olarak da
 * okunur ("6,6 → 7,0 → 7,4"). Son nokta dolu daire.
 */
@Component({
  selector: 'app-sparkline',
  template: `
    <svg
      [attr.width]="genislik()"
      [attr.height]="yukseklik()"
      [attr.viewBox]="'0 0 ' + genislik() + ' ' + yukseklik()"
      role="img"
      [attr.aria-label]="'Trend: ' + etiket()"
      class="inline-block align-middle text-slate-600"
    >
      @if (noktalar().length > 1) {
        <polyline [attr.points]="cizgi()" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" />
      }
      @if (noktalar().at(-1); as s) {
        <circle [attr.cx]="s.x" [attr.cy]="s.y" r="2.5" fill="currentColor" />
      }
    </svg>
  `,
})
export class Sparkline {
  readonly degerler = input.required<number[]>();
  /** Ekran okuyucu metni. */
  readonly etiket = input.required<string>();
  readonly genislik = input(64);
  readonly yukseklik = input(20);

  protected readonly noktalar = computed(() => {
    const d = this.degerler();
    const [w, h, pay] = [this.genislik(), this.yukseklik(), 3];
    const min = Math.min(...d);
    const max = Math.max(...d);
    const aralik = max - min || 1;
    return d.map((v, i) => ({
      x: d.length === 1 ? w - pay : pay + (i * (w - 2 * pay)) / (d.length - 1),
      // Değer aynıysa çizgi ortada düz kalır.
      y: max === min ? h / 2 : h - pay - ((v - min) * (h - 2 * pay)) / aralik,
    }));
  });

  protected readonly cizgi = computed(() =>
    this.noktalar()
      .map((n) => `${n.x.toFixed(1)},${n.y.toFixed(1)}`)
      .join(' '),
  );
}
