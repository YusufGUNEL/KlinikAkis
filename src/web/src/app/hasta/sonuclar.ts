import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { basamak, HASTA_UYARI_METNI, HastaSonucu, ISARET_ETIKETI, trendMetni } from '../cekirdek/tahlil';
import { AiKarti } from '../ortak/ai-karti';
import { AralikCubugu } from '../ortak/aralik-cubugu';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { Sparkline } from '../ortak/sparkline';

/**
 * Hasta "Sonuçlarım" (taslak 3). Yalnızca hekimin yayınladığı sonuçlar ve metin gelir (sunucu süzer; AI taslağı bu
 * uçtan hiç dönmez). Değer + hasta etiketi (§10.1), aralık çubuğu, açıklama (mor kart, hekim onaylı), trend ve sabit
 * uyarı. Hekim yayınlayınca TahlilYayinlandi olayıyla sayfa yenilenmeden güncellenir.
 */
@Component({
  selector: 'app-sonuclar',
  imports: [AiKarti, AralikCubugu, BaglantiGostergesi, DatePipe, DecimalPipe, DurumKutusu, RouterLink, Sparkline],
  template: `
    <a routerLink=".." class="-ml-2 inline-flex min-h-12 items-center rounded-lg px-2 font-medium text-ana">
      <span aria-hidden="true">←</span>&nbsp;Ana sayfa
    </a>
    <div class="mt-1 flex items-start justify-between gap-3">
      <h1 class="text-2xl font-semibold text-slate-900">Sonuçlarım</h1>
      <app-baglanti-gostergesi [durum]="hub.durum()" />
    </div>

    <div class="mt-4 flex flex-col gap-4" aria-live="polite">
      @if (sonuclar.error()) {
        <app-durum-kutusu tur="hata" (yenidenDene)="sonuclar.reload()" />
      } @else if (sonuclar.hasValue()) {
        @for (s of sonuclar.value(); track s.sonucId) {
          @let e = etiket[s.isaret];
          <article class="rounded-lg border border-slate-200 p-4" [attr.aria-labelledby]="'sonuc-' + s.sonucId" data-test="sonuc">
            <h2 [id]="'sonuc-' + s.sonucId" class="text-lg font-semibold text-slate-900">
              {{ s.hastaDostuAd }} <span class="font-normal text-slate-700">({{ s.ad }})</span>
            </h2>
            <p class="mt-1 flex flex-wrap items-baseline gap-x-3">
              <span class="text-2xl font-semibold tabular-nums">{{ s.deger | number: basamak(s.ondalik) }} {{ s.birim }}</span>
              <span class="font-semibold" [class]="e.renk" data-test="hasta-etiket">{{ e.hasta }}</span>
            </p>
            <p class="text-base text-slate-700 tabular-nums">Alındı: {{ s.zaman | date: 'dd.MM.yyyy HH:mm' }}</p>
            <app-aralik-cubugu [deger]="s.deger" [alt]="s.altSinir" [ust]="s.ustSinir" [ondalik]="s.ondalik" [birim]="s.birim" [isaret]="s.isaret" />

            @if (s.aciklama) {
              <div class="mt-3">
                <app-ai-karti
                  hedef="hasta"
                  [zaman]="s.yayinZamani"
                  [onay]="{ hekimAd: s.yayinlayanHekim ?? 'Doktorunuz', zaman: s.yayinZamani }"
                >
                  <p class="whitespace-pre-line" data-test="aciklama">{{ s.aciklama }}</p>
                </app-ai-karti>
                <p class="mt-2 text-base text-slate-700" data-test="uyari">{{ uyariMetni }}</p>
              </div>
            }

            @if (s.trend.length > 1) {
              <p class="mt-3 flex flex-wrap items-center gap-2 text-slate-700">
                <span>Trend: <span class="tabular-nums">{{ trend(s) }}</span></span>
                <app-sparkline [degerler]="degerler(s)" [etiket]="trend(s)" [genislik]="88" [yukseklik]="28" />
              </p>
            }
          </article>
        } @empty {
          <app-durum-kutusu tur="bos" mesaj="Henüz doktorunuzun paylaştığı bir sonuç yok." />
        }
        @if (sonuclar.value().length) {
          <p class="text-base text-slate-700">Referans aralıkları örnektir, laboratuvara göre değişir.</p>
        }
      } @else {
        <app-durum-kutusu tur="yukleniyor" />
      }
    </div>
  `,
})
export class Sonuclar {
  protected readonly hub = inject(HubService);
  protected readonly sonuclar = httpResource<HastaSonucu[]>(() => '/api/hasta/me/sonuclar');
  protected readonly etiket = ISARET_ETIKETI;
  protected readonly basamak = basamak;
  protected readonly uyariMetni = HASTA_UYARI_METNI;

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.sonuclar);
  }

  protected degerler(s: HastaSonucu): number[] {
    return s.trend.map((t) => t.deger);
  }

  protected trend(s: HastaSonucu): string {
    return trendMetni(this.degerler(s), s.ondalik);
  }
}
