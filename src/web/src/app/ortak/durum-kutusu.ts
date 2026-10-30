import { Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';

export type KutuTuru = 'bos' | 'yukleniyor' | 'hata';

/**
 * Boş / yükleniyor / hata kalıbı (SPEC §10.1: her ekranda üçü de). Renk tek ipucu değil: ikon + metin.
 * Düz kenar: kesik çizgi AI taslak kartının imzasıdır, başka yerde kullanılmaz.
 */
@Component({
  selector: 'app-durum-kutusu',
  imports: [ButtonModule],
  template: `
    <div
      class="flex flex-col items-center gap-3 rounded-lg border border-slate-200 bg-slate-50 px-4 py-8 text-center text-slate-700"
      [attr.role]="tur() === 'hata' ? 'alert' : 'status'"
    >
      <span class="text-2xl" aria-hidden="true">{{ ikon() }}</span>
      <p>{{ mesaj() || varsayilan() }}</p>
      @if (tur() === 'hata' && yenidenDenenebilir()) {
        <button pButton type="button" class="min-h-12" (click)="yenidenDene.emit()">Yeniden dene</button>
      }
    </div>
  `,
})
export class DurumKutusu {
  readonly tur = input.required<KutuTuru>();
  readonly mesaj = input('');
  readonly yenidenDenenebilir = input(true);
  readonly yenidenDene = output();

  protected ikon(): string {
    return { bos: '○', yukleniyor: '…', hata: '✗' }[this.tur()];
  }

  protected varsayilan(): string {
    return {
      bos: 'Gösterilecek kayıt yok.',
      yukleniyor: 'Yükleniyor…',
      hata: 'Veri alınamadı. Bağlantıyı kontrol edip yeniden deneyin.',
    }[this.tur()];
  }
}
