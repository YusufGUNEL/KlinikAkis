import { DatePipe } from '@angular/common';
import { Component } from '@angular/core';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { simdiSinyali } from '../ortak/simdi';
import { UstCubuk } from '../ortak/ust-cubuk';

/**
 * Doktor çalışma alanı, 3 sütun (taslak 4, Epic Storyboard düzeni): sol günün listesi, orta hasta bandı +
 * sekmeler, sağ sessiz uyarılar / modele giden veri / erişim kaydı. Liste ve hasta seçimi Aşama 3'te gelir.
 */
@Component({
  selector: 'app-doktor',
  imports: [DatePipe, DurumKutusu, UstCubuk],
  template: `
    <div class="flex h-dvh flex-col text-sm">
      <app-ust-cubuk ekran="Doktor" />
      <div class="grid min-h-0 flex-1 grid-cols-[16rem_minmax(0,1fr)_18rem]">
        <nav class="overflow-y-auto border-r border-slate-200 bg-white p-3" aria-labelledby="bugun-baslik">
          <h2 id="bugun-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">
            Bugün · <span class="tabular-nums">{{ bugun() | date }}</span>
          </h2>
          <div class="mt-3">
            <app-durum-kutusu tur="bos" mesaj="Bugün listede randevu yok." />
          </div>
        </nav>

        <main class="overflow-y-auto p-4">
          <app-durum-kutusu tur="bos" mesaj="Listeden bir hasta seçin." />
        </main>

        <aside class="overflow-y-auto border-l border-slate-200 bg-white p-3" aria-label="Yan panel">
          @for (bolum of yanBolumler; track bolum.baslik) {
            <section class="border-b border-slate-100 py-3 first:pt-0" [attr.aria-label]="bolum.baslik">
              <h2 class="text-xs font-semibold tracking-wide text-slate-600 uppercase">{{ bolum.baslik }}</h2>
              <p class="mt-1 text-slate-700">{{ bolum.bos }}</p>
            </section>
          }
        </aside>
      </div>
    </div>
  `,
})
export class Doktor {
  protected readonly bugun = simdiSinyali();

  // Başlıklar CSS uppercase ile büyür: lang="tr" olduğu için "Erişim" → "ERİŞİM" doğru çıkar (SPEC §10.5).
  protected readonly yanBolumler = [
    { baslik: 'Uyarılar', bos: 'Uyarı yok.' },
    { baslik: 'Modele giden veri', bos: 'Hasta seçilmedi.' },
    { baslik: 'Erişim kaydı', bos: 'Hasta seçilmedi.' },
  ];
}
