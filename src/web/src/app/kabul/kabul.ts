import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, ElementRef, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { OutboxSatiri, RandevuDurumu, RandevuEylemi, RandevuSatiri } from '../cekirdek/api';
import { Demo } from '../cekirdek/demo';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { EYLEM_ETIKETI, hataMesaji, RandevuIslemleri, saatEtiketi } from '../cekirdek/randevu';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { DurumRozeti } from '../ortak/durum-rozeti';
import { GonderimKutusu } from '../ortak/gonderim-kutusu';
import { simdiSinyali } from '../ortak/simdi';
import { UstCubuk } from '../ortak/ust-cubuk';

const KABUL_EYLEMLERI: RandevuEylemi[] = ['Kabul', 'Gelmedi', 'Iptal'];
const DURUM_ADI: Record<RandevuDurumu, string> = {
  Planlandi: 'planlandı',
  KabulEdildi: 'kabul edildi',
  Cagrildi: 'çağrıldı',
  Muayenede: 'muayenede',
  Tamamlandi: 'tamamlandı',
  Iptal: 'iptal edildi',
  Gelmedi: 'gelmedi olarak işaretlendi',
};

/** Kabul ekranı (taslak 7): günün randevuları canlı, isimler sunucuda maskelenmiş gelir. */
@Component({
  selector: 'app-kabul',
  imports: [BaglantiGostergesi, ButtonModule, DatePipe, DurumKutusu, DurumRozeti, GonderimKutusu, UstCubuk],
  template: `
    <div class="flex min-h-dvh flex-col text-sm">
      <app-ust-cubuk ekran="Kabul"><app-baglanti-gostergesi [durum]="hub.durum()" /></app-ust-cubuk>
      <main class="mx-auto w-full max-w-6xl p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h1 class="text-xl font-semibold text-slate-900">
            Kabul · Bugün <span class="tabular-nums">{{ bugun() | date }}</span>
          </h1>
          <span class="flex items-center gap-3">
            @if (gonderimHatasi()) {
              <!-- Tablo uzun; kutu en altta. İş kuralı hatası üstte duyurulur, tıklayınca kutuya gider (amber, kırmızı değil). -->
              <button type="button" class="rounded px-1 font-medium text-uyari underline" data-test="gonderim-hatasi" (click)="kutuyaGit()">
                <span aria-hidden="true">⚠</span> {{ gonderimHatasi() }} iş kuralı hatası
              </button>
            }
            @if (mesaj()) {
              <span class="text-slate-700" role="status">{{ mesaj() }}</span>
            }
            <button pButton type="button" severity="secondary" variant="outlined" class="text-slate-700" [disabled]="sifirlaniyor()" (click)="sifirla()">
              {{ sifirlaniyor() ? 'Sıfırlanıyor…' : 'Demo sıfırla' }}
            </button>
          </span>
        </div>

        @if (hata()) {
          <p class="mt-3 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
        }

        <p class="sr-only" role="status">{{ duyuru() }}</p>

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
            @if (liste.error()) {
              <tr><td [attr.colspan]="sutunlar.length" class="p-3"><app-durum-kutusu tur="hata" (yenidenDene)="liste.reload()" /></td></tr>
            } @else if (liste.hasValue()) {
              @for (r of liste.value(); track r.id) {
                <tr class="border-b border-slate-100 align-top" tabindex="-1" [attr.data-id]="r.id">
                  <td class="px-3 py-2 whitespace-nowrap tabular-nums">{{ saat(r.baslangic) }} · {{ r.siraNo }}</td>
                  <td class="px-3 py-2">
                    <span class="font-medium text-slate-900">{{ r.hastaAd }}</span>
                    @if (r.takipNo) {
                      <span class="mt-1 block text-slate-700">
                        Açık rıza: {{ r.acikRizaAlindi ? '☑ alındı' : '☐ alınmadı' }} · Takip no:
                        <span class="tabular-nums">{{ r.takipNo }}</span> (simülasyon)
                      </span>
                    }
                  </td>
                  <td class="px-3 py-2">{{ r.bransAd }}</td>
                  <td class="px-3 py-2">{{ r.hekimAd }}</td>
                  <td class="px-3 py-2"><app-durum-rozeti [durum]="r.durum" /></td>
                  <td class="px-3 py-1">
                    <span class="flex gap-1 whitespace-nowrap">
                      @for (e of kabulEylemleri; track e) {
                        @if (r.izinliEylemler.includes(e)) {
                          <button
                            pButton
                            type="button"
                            size="small"
                            [severity]="e === 'Kabul' ? 'primary' : 'secondary'"
                            [variant]="e === 'Kabul' ? undefined : 'outlined'"
                            [disabled]="islemdekiler().has(r.id)"
                            [attr.aria-label]="r.siraNo + ' ' + r.hastaAd + ': ' + etiketler[e]"
                            (click)="uygula(r, e)"
                          >
                            {{ etiketler[e] }}
                          </button>
                        }
                      }
                    </span>
                  </td>
                </tr>
              } @empty {
                <tr><td [attr.colspan]="sutunlar.length" class="p-3"><app-durum-kutusu tur="bos" mesaj="Bugün kabul bekleyen randevu yok." /></td></tr>
              }
            } @else {
              <tr><td [attr.colspan]="sutunlar.length" class="p-3"><app-durum-kutusu tur="yukleniyor" /></td></tr>
            }
          </tbody>
        </table>

        <app-gonderim-kutusu class="mt-8 block" [liste]="outbox" />
      </main>
    </div>
  `,
})
export class Kabul {
  private readonly demo = inject(Demo);
  private readonly islemler = inject(RandevuIslemleri);
  protected readonly hub = inject(HubService);

  protected readonly bugun = simdiSinyali();
  protected readonly sutunlar = ['Saat · Sıra', 'Hasta (maskeli)', 'Bölüm', 'Doktor', 'Durum', 'İşlem'];
  protected readonly kabulEylemleri = KABUL_EYLEMLERI;
  protected readonly etiketler = EYLEM_ETIKETI;
  protected readonly sifirlaniyor = signal(false);
  protected readonly mesaj = signal('');
  protected readonly hata = signal('');
  /** Satır başına: bir satırın işlemi bitince başka satırın butonları erken açılmasın. */
  protected readonly islemdekiler = signal<ReadonlySet<string>>(new Set());
  /** Ekran okuyucuya kısa özet; tablonun tamamı canlı bölge değil (her yeniden çekmede okunmasın). */
  protected readonly duyuru = signal('');
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly liste = httpResource<RandevuSatiri[]>(() => '/api/randevular');
  protected readonly outbox = httpResource<OutboxSatiri[]>(() => '/api/outbox');
  protected readonly gonderimHatasi = computed(() =>
    this.outbox.hasValue() ? (this.outbox.value() ?? []).filter((p) => p.durum === 'IsKuraliHatasi').length : 0,
  );

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.liste, this.outbox);
  }

  protected kutuyaGit(): void {
    const baslik = this.kok.nativeElement.querySelector<HTMLElement>('#gonderim-baslik');
    baslik?.scrollIntoView({ block: 'start' });
    baslik?.focus();
  }

  protected saat(iso: string): string {
    return saatEtiketi(iso, this.bugun());
  }

  protected async uygula(r: RandevuSatiri, eylem: RandevuEylemi): Promise<void> {
    this.islemdekiler.update((s) => new Set(s).add(r.id));
    this.hata.set('');
    try {
      const sonuc = await this.islemler.uygula(r.id, eylem);
      this.duyuru.set(`${sonuc.siraNo} ${sonuc.hastaAd}: ${DURUM_ADI[sonuc.durum]}`);
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.islemdekiler.update((s) => {
        const yeni = new Set(s);
        yeni.delete(r.id);
        return yeni;
      });
      this.liste.reload();
      // Basılan buton kalkıyor; klavye odağı body'ye düşmesin, satıra dönsün.
      this.kok.nativeElement.querySelector<HTMLElement>(`tr[data-id="${r.id}"]`)?.focus();
    }
  }

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
