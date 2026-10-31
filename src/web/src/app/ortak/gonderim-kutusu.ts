import { DatePipe } from '@angular/common';
import { HttpResourceRef } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { OutboxDurumu, OutboxPaketTipi, OutboxSatiri } from '../cekirdek/api';
import { OutboxIslemleri } from '../cekirdek/outbox';
import { hataMesaji } from '../cekirdek/randevu';
import { DurumKutusu } from './durum-kutusu';

export const PAKET_ADI: Record<OutboxPaketTipi, string> = {
  HastaKayit: '101 Hasta Kayıt',
  Muayene: '103 Muayene',
  LabSonuc: '105 Lab Sonuç',
};

/** Renk tek ipucu değil: ikon + metin (SPEC §10.1). İş kuralı hatası amber; kırmızı yalnız klinik uyarıya ayrılı. */
export const OUTBOX_DURUMU: Record<OutboxDurumu, { ikon: string; metin: string; renk: string }> = {
  Gonderildi: { ikon: '✓', metin: 'Gönderildi', renk: 'text-basari' },
  IsKuraliHatasi: { ikon: '⚠', metin: 'İş kuralı', renk: 'text-uyari' },
  Bekliyor: { ikon: '◌', metin: 'Bekliyor', renk: 'text-slate-600' },
};

/**
 * e-Nabız gönderim kutusu (taslak 9, SPEC §5.2). Gerçek gönderim yok: "SİMÜLASYON" etiketi her zaman görünür.
 * Liste üst bileşenin kaynağıdır (kabulde tümü, doktorda seçili hastanınki); kutu yalnızca gösterir ve yeniden dener.
 * `kompakt`: doktor yan paneli için tablo yerine satır listesi.
 */
@Component({
  selector: 'app-gonderim-kutusu',
  imports: [ButtonModule, DatePipe, DurumKutusu],
  template: `
    <section [attr.aria-labelledby]="baslikId()">
      <h2 [id]="baslikId()" tabindex="-1" class="flex items-center gap-2" [class]="kompakt() ? 'text-xs font-semibold tracking-wide text-slate-600 uppercase' : 'text-base font-semibold text-slate-900'">
        e-Nabız gönderim kutusu
        <span class="rounded border border-slate-500 px-1 text-[11px] font-semibold tracking-wide text-slate-700 normal-case">SİMÜLASYON</span>
      </h2>

      @if (liste().hasValue() && liste().value()!.length) {
        @if (kompakt()) {
          <ul class="mt-1 flex flex-col gap-2" data-test="gonderim-listesi">
            @for (p of liste().value(); track p.id) {
              <li class="text-slate-700">
                <span class="tabular-nums">{{ p.zaman | date: 'HH:mm' }}</span> · {{ paketAdi[p.paketTipi] }} ·
                <span class="font-medium" [class]="durumlar[p.durum].renk"><span aria-hidden="true">{{ durumlar[p.durum].ikon }} </span>{{ durumlar[p.durum].metin }}</span>
                @if (p.hata) {
                  <span class="block text-xs text-slate-600">{{ p.hata }}</span>
                }
                @if (p.durum === 'IsKuraliHatasi') {
                  <button pButton type="button" size="small" variant="outlined" class="ikincil mt-1" [attr.aria-label]="etiket(p)" [disabled]="islemde().has(p.id)" (click)="yenidenDene(p)">
                    Yeniden dene
                  </button>
                }
              </li>
            }
          </ul>
        } @else {
          <table class="mt-2 w-full border-collapse bg-white text-[13px]" data-test="gonderim-listesi">
            <caption class="sr-only">e-Nabız gönderim kutusu (simülasyon)</caption>
            <thead>
              <tr class="border-b border-slate-200 text-left text-slate-600">
                @for (s of sutunlar; track s) {
                  <th scope="col" class="h-9 px-3 font-semibold">{{ s }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (p of liste().value(); track p.id) {
                <tr class="h-9 border-b border-slate-100">
                  <td class="px-3">{{ paketAdi[p.paketTipi] }}</td>
                  <td class="px-3 font-mono">{{ p.hastaTakmaAdi }}</td>
                  <td class="px-3">
                    <span class="font-medium" [class]="durumlar[p.durum].renk"><span aria-hidden="true">{{ durumlar[p.durum].ikon }} </span>{{ durumlar[p.durum].metin }}</span>
                    @if (p.hata) {
                      <span class="ml-2 text-slate-600">{{ p.hata }}</span>
                    }
                  </td>
                  <td class="px-3 tabular-nums">{{ p.zaman | date: 'HH:mm' }}</td>
                  <td class="px-3">
                    @if (p.durum === 'IsKuraliHatasi') {
                      <button pButton type="button" size="small" variant="outlined" class="ikincil" [attr.aria-label]="etiket(p)" [disabled]="islemde().has(p.id)" (click)="yenidenDene(p)">
                        Yeniden dene
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      } @else if (liste().error() && kompakt()) {
        <!-- Yan panelin öteki bölümleriyle aynı küçük kalıp. -->
        <p class="mt-1 text-uyari" role="alert"><span aria-hidden="true">⚠</span> Yüklenemedi.</p>
        <button pButton type="button" size="small" variant="outlined" class="ikincil mt-2" (click)="liste().reload()">Yeniden dene</button>
      } @else if (liste().error()) {
        <app-durum-kutusu tur="hata" mesaj="Gönderim kutusu alınamadı." (yenidenDene)="liste().reload()" />
      } @else if (liste().isLoading()) {
        <p class="mt-1 text-slate-600">Yükleniyor…</p>
      } @else {
        <p class="mt-1 text-slate-700">{{ bosMesaj() }}</p>
      }

      @if (hata()) {
        <p class="mt-1 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }
    </section>
  `,
})
export class GonderimKutusu {
  private readonly islemler = inject(OutboxIslemleri);

  readonly liste = input.required<HttpResourceRef<OutboxSatiri[] | undefined>>();
  readonly kompakt = input(false);
  readonly bosMesaj = input('Henüz paket yok.');
  readonly baslikId = input('gonderim-baslik');

  protected readonly paketAdi = PAKET_ADI;
  protected readonly durumlar = OUTBOX_DURUMU;
  protected readonly sutunlar = ['Paket', 'Hasta', 'Durum', 'Zaman', 'İşlem'];
  protected readonly islemde = signal<ReadonlySet<string>>(new Set());
  protected readonly hata = signal('');

  /** Ekran okuyucu: birden çok hatalı pakette düğmeler ayırt edilsin. */
  protected etiket(p: OutboxSatiri): string {
    return `${PAKET_ADI[p.paketTipi]} ${p.hastaTakmaAdi}: yeniden dene`;
  }

  protected async yenidenDene(p: OutboxSatiri): Promise<void> {
    this.islemde.update((s) => new Set(s).add(p.id));
    this.hata.set('');
    try {
      await this.islemler.yenidenDene(p.id);
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.islemde.update((s) => {
        const yeni = new Set(s);
        yeni.delete(p.id);
        return yeni;
      });
      this.liste().reload();
    }
  }
}
