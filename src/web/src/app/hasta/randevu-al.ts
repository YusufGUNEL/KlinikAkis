import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { BransOgesi, SlotOgesi } from '../cekirdek/api';
import { gunEtiketi, hataMesaji, RandevuIslemleri } from '../cekirdek/randevu';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { simdiSinyali } from '../ortak/simdi';

/**
 * Randevu alma: bölüm seç → saat seç → onayla. Şikayet sohbetinden gelindiyse (?onAnamnez=) randevu o ön
 * anamneze bağlanır. Bölüm şimdilik elle seçilir; Aşama 5'te sohbetin önerdiği bölüm ön seçili gelir.
 */
@Component({
  selector: 'app-randevu-al',
  imports: [ButtonModule, DurumKutusu, RouterLink],
  template: `
    <a routerLink=".." class="inline-flex min-h-12 items-center text-base font-medium text-ana">← Randevularım</a>
    <h1 class="mt-2 text-2xl font-semibold text-slate-900">Randevu al</h1>

    @if (!brans()) {
      <h2 class="mt-5 text-lg font-semibold text-slate-900">1. Bölüm seçin</h2>
      <div class="mt-3 flex flex-col gap-2">
        @if (branslar.error()) {
          <app-durum-kutusu tur="hata" (yenidenDene)="branslar.reload()" />
        } @else if (branslar.hasValue()) {
          @for (b of acikBranslar(); track b.id) {
            <button
              type="button"
              class="min-h-12 rounded-lg border border-slate-300 px-4 text-left font-medium text-slate-900 hover:border-ana hover:bg-slate-50"
              (click)="bransSec(b)"
            >
              {{ b.ad }}
            </button>
          }
        } @else {
          <app-durum-kutusu tur="yukleniyor" />
        }
      </div>
    } @else {
      <p class="mt-5 flex items-center justify-between gap-2">
        <span class="font-semibold text-slate-900">{{ brans()!.ad }}</span>
        <button type="button" class="min-h-12 px-2 text-base font-medium text-ana" aria-label="Bölümü değiştir" (click)="bransSec(null)">Değiştir</button>
      </p>

      <h2 class="mt-3 text-lg font-semibold text-slate-900">2. Saat seçin</h2>
      <div class="mt-3 flex flex-col gap-2" role="radiogroup" aria-label="Uygun saatler">
        @if (slotlar.error()) {
          <app-durum-kutusu tur="hata" (yenidenDene)="slotlar.reload()" />
        } @else if (slotlar.hasValue()) {
          @for (s of slotlar.value(); track s.id) {
            <button
              type="button"
              role="radio"
              [attr.aria-checked]="secili()?.id === s.id"
              class="min-h-12 rounded-lg border px-4 py-2 text-left"
              [class]="secili()?.id === s.id ? 'border-ana bg-ana/10 font-semibold text-slate-900' : 'border-slate-500 text-slate-900'"
              (click)="secili.set(s)"
            >
              <span class="tabular-nums">{{ etiket(s.baslangic) }}</span> · {{ s.hekimAd }}
              @if (secili()?.id === s.id) {
                <span class="text-ana"> ✓ seçildi</span>
              }
            </button>
          } @empty {
            <app-durum-kutusu tur="bos" mesaj="Bu bölümde uygun saat yok. Başka bir bölüm seçin." />
          }
        } @else {
          <app-durum-kutusu tur="yukleniyor" />
        }
      </div>

      @if (hata()) {
        <p class="mt-3 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }

      <button
        pButton
        type="button"
        class="sticky bottom-4 mt-5 min-h-12 w-full justify-center text-lg"
        [disabled]="!secili() || gonderiliyor()"
        (click)="onayla()"
      >
        {{ gonderiliyor() ? 'Gönderiliyor…' : 'Randevuyu onayla' }}
      </button>
    }
  `,
})
export class RandevuAl {
  private readonly islemler = inject(RandevuIslemleri);
  private readonly router = inject(Router);
  private readonly onAnamnezId = inject(ActivatedRoute).snapshot.queryParamMap.get('onAnamnez');

  private readonly simdi = simdiSinyali();
  protected readonly brans = signal<BransOgesi | null>(null);
  protected readonly secili = signal<SlotOgesi | null>(null);
  protected readonly gonderiliyor = signal(false);
  protected readonly hata = signal('');

  protected readonly branslar = httpResource<BransOgesi[]>(() => '/api/branslar');
  protected readonly acikBranslar = computed(() =>
    this.branslar.hasValue() ? this.branslar.value().filter((b) => b.hekimSayisi > 0) : [],
  );
  protected readonly slotlar = httpResource<SlotOgesi[]>(() => {
    const b = this.brans();
    return b ? { url: '/api/slotlar', params: { bransId: b.id } } : undefined;
  });

  protected bransSec(b: BransOgesi | null): void {
    this.brans.set(b);
    this.secili.set(null);
    this.hata.set('');
  }

  protected etiket(iso: string): string {
    return gunEtiketi(iso, this.simdi());
  }

  protected async onayla(): Promise<void> {
    const slot = this.secili();
    if (!slot) return;
    this.gonderiliyor.set(true);
    this.hata.set('');
    try {
      await this.islemler.al(slot.id, this.onAnamnezId);
      await this.router.navigate(['/hasta']);
    } catch (e) {
      // 409: saat bu arada doldu; liste yenilenir, hasta başka saat seçer.
      this.hata.set(hataMesaji(e));
      this.secili.set(null);
      this.slotlar.reload();
    } finally {
      this.gonderiliyor.set(false);
    }
  }
}
