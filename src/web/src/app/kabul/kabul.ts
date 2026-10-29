import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { Demo } from '../cekirdek/demo';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { simdiSinyali } from '../ortak/simdi';
import { UstCubuk } from '../ortak/ust-cubuk';

/** Kabul ekranı (taslak 7). Günün randevuları ve "Kabul et" Aşama 3'te gelir. */
@Component({
  selector: 'app-kabul',
  imports: [ButtonModule, DatePipe, DurumKutusu, UstCubuk],
  template: `
    <div class="flex min-h-dvh flex-col text-sm">
      <app-ust-cubuk ekran="Kabul" />
      <main class="mx-auto w-full max-w-6xl p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h1 class="text-xl font-semibold text-slate-900">
            Kabul · Bugün <span class="tabular-nums">{{ bugun() | date }}</span>
          </h1>
          <span class="flex items-center gap-3">
            @if (mesaj()) {
              <span class="text-slate-700" role="status">{{ mesaj() }}</span>
            }
            <button pButton type="button" severity="secondary" variant="outlined" class="text-slate-700" [disabled]="sifirlaniyor()" (click)="sifirla()">
              {{ sifirlaniyor() ? 'Sıfırlanıyor…' : 'Demo sıfırla' }}
            </button>
          </span>
        </div>

        <table class="mt-4 w-full border-collapse bg-white text-[13px]">
          <caption class="sr-only">Günün randevuları</caption>
          <thead>
            <tr class="border-b border-slate-200 text-left text-slate-600">
              @for (sutun of sutunlar; track sutun) {
                <th scope="col" class="h-9 px-3 font-semibold">{{ sutun }}</th>
              }
            </tr>
          </thead>
          <tbody>
            <tr>
              <td [attr.colspan]="sutunlar.length" class="p-3">
                <app-durum-kutusu tur="bos" mesaj="Bugün kabul bekleyen randevu yok." />
              </td>
            </tr>
          </tbody>
        </table>
      </main>
    </div>
  `,
})
export class Kabul {
  private readonly demo = inject(Demo);

  protected readonly bugun = simdiSinyali();
  protected readonly sutunlar = ['Saat', 'Hasta (maskeli)', 'Bölüm', 'Doktor', 'Durum', 'İşlem'];
  protected readonly sifirlaniyor = signal(false);
  protected readonly mesaj = signal('');

  protected async sifirla(): Promise<void> {
    this.sifirlaniyor.set(true);
    try {
      await this.demo.sifirla();
      this.mesaj.set('✓ Demo verisi sıfırlandı');
    } catch {
      this.mesaj.set('✗ Sıfırlanamadı');
    } finally {
      this.sifirlaniyor.set(false);
    }
  }
}
