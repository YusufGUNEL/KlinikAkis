import { Component, computed, inject } from '@angular/core';
import { Oturum } from '../cekirdek/oturum';
import { DurumKutusu } from '../ortak/durum-kutusu';

/** Hasta ana sayfası. Randevu listesi (GET /hasta/me/randevular) Aşama 3'te bağlanır. */
@Component({
  selector: 'app-hasta-ana',
  imports: [DurumKutusu],
  template: `
    <h1 class="text-2xl font-semibold text-slate-900">Merhaba, {{ ad() }}</h1>
    <section class="mt-5" aria-labelledby="randevular-baslik">
      <h2 id="randevular-baslik" class="text-lg font-semibold text-slate-900">Randevularım</h2>
      <div class="mt-3">
        <app-durum-kutusu tur="bos" mesaj="Henüz randevunuz yok." />
      </div>
    </section>
  `,
})
export class HastaAna {
  private readonly oturum = inject(Oturum);
  protected readonly ad = computed(() => this.oturum.aktif()?.ad ?? '');
}
