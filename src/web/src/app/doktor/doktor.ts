import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, ElementRef, inject, signal } from '@angular/core';
import { QRCodeComponent } from 'angularx-qrcode';
import { ButtonModule } from 'primeng/button';
import { HastaOzeti, RandevuEylemi, RandevuSatiri } from '../cekirdek/api';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { EYLEM_ETIKETI, hataMesaji, RandevuIslemleri, saatEtiketi } from '../cekirdek/randevu';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { DurumRozeti } from '../ortak/durum-rozeti';
import { HastaBandi } from '../ortak/hasta-bandi';
import { simdiSinyali } from '../ortak/simdi';
import { UstCubuk } from '../ortak/ust-cubuk';

/** QR'ın açtığı senaryo hastası (Ayşe K., SPEC §6). */
const QR_HASTA_ID = 'a1000000-0000-0000-0000-000000000001';
const DOKTOR_EYLEMLERI: RandevuEylemi[] = ['Cagir', 'Basla', 'Kapat'];

/**
 * Doktor çalışma alanı, 3 sütun (taslak 4, Epic Storyboard düzeni): sol günün listesi (canlı), orta hasta
 * bandı + eylem çubuğu, sağ sessiz uyarılar / modele giden veri / erişim kaydı (sonraki aşamalar).
 * Butonların etkinliği sunucudaki durum makinesinden gelen `izinliEylemler`dir; kural istemcide tekrarlanmaz.
 */
@Component({
  selector: 'app-doktor',
  imports: [BaglantiGostergesi, ButtonModule, DatePipe, DurumKutusu, DurumRozeti, HastaBandi, QRCodeComponent, UstCubuk],
  template: `
    <div class="flex h-dvh flex-col text-sm">
      <app-ust-cubuk ekran="Doktor"><app-baglanti-gostergesi [durum]="hub.durum()" /></app-ust-cubuk>
      <div class="grid min-h-0 flex-1 grid-cols-[16rem_minmax(0,1fr)_18rem]">
        <nav class="flex min-h-0 flex-col border-r border-slate-200 bg-white" aria-labelledby="bugun-baslik">
          <div class="min-h-0 flex-1 overflow-y-auto p-3">
            <h2 id="bugun-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">
              Bugün · <span class="tabular-nums">{{ bugun() | date }}</span>
            </h2>
            <div class="mt-3">
              @if (liste.error()) {
                <app-durum-kutusu tur="hata" (yenidenDene)="liste.reload()" />
              } @else if (liste.hasValue()) {
                <ul class="flex flex-col gap-1">
                  @for (r of liste.value(); track r.id) {
                    <li>
                      <button
                        type="button"
                        class="w-full rounded-md px-2 py-2 text-left hover:bg-slate-50"
                        [class.bg-slate-100]="seciliId() === r.id"
                        [class.ring-1]="seciliId() === r.id"
                        [class.ring-ana]="seciliId() === r.id"
                        [attr.aria-current]="seciliId() === r.id ? 'true' : null"
                        (click)="sec(r)"
                      >
                        <span class="flex items-center justify-between gap-2">
                          <span class="tabular-nums text-slate-700">{{ saat(r.baslangic) }} · {{ r.siraNo }}</span>
                          <app-durum-rozeti [durum]="r.durum" />
                        </span>
                        <span class="block font-medium text-slate-900">{{ r.hastaAd }}</span>
                      </button>
                    </li>
                  } @empty {
                    <li><app-durum-kutusu tur="bos" mesaj="Bugün listede randevu yok." /></li>
                  }
                </ul>
              } @else {
                <app-durum-kutusu tur="yukleniyor" />
              }
            </div>
          </div>
          <figure class="flex items-center gap-3 border-t border-slate-200 p-3">
            <qrcode elementType="svg" [qrdata]="qrAdresi" [width]="96" [margin]="1" errorCorrectionLevel="M" ariaLabel="Hasta girişi QR kodu" />
            <figcaption class="text-xs text-slate-700">Telefonla okutun: Ayşe K. olarak hasta ekranı açılır.</figcaption>
          </figure>
        </nav>

        <main class="min-h-0 overflow-y-auto">
          @if (secili(); as r) {
            @if (ozet.hasValue()) {
              <app-hasta-bandi [ozet]="ozet.value()" />
            } @else if (ozet.error()) {
              <div class="p-4"><app-durum-kutusu tur="hata" (yenidenDene)="ozet.reload()" /></div>
            } @else {
              <div class="p-4"><app-durum-kutusu tur="yukleniyor" /></div>
            }
            <div class="flex flex-wrap items-center gap-2 border-b border-slate-200 bg-white px-4 py-2" role="toolbar" aria-label="Randevu işlemleri" tabindex="-1">
              <span class="mr-2 text-slate-700">
                <span class="tabular-nums">{{ saat(r.baslangic) }} · {{ r.siraNo }}</span> ·
                <app-durum-rozeti [durum]="r.durum" />
              </span>
              @for (e of doktorEylemleri; track e) {
                <button
                  pButton
                  type="button"
                  [severity]="r.izinliEylemler.includes(e) ? 'primary' : 'secondary'"
                  [variant]="r.izinliEylemler.includes(e) ? undefined : 'outlined'"
                  [disabled]="!r.izinliEylemler.includes(e) || islemde()"
                  (click)="uygula(r, e)"
                >
                  {{ etiketler[e] }}
                </button>
              }
              <span class="sr-only" role="status">{{ duyuru() }}</span>
              @if (hata()) {
                <span class="font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</span>
              }
            </div>
            @if (r.durum === 'Planlandi') {
              <p class="px-4 py-3 text-slate-700">Hasta henüz kabul edilmedi; çağırmak için kabul masasını bekleyin.</p>
            }
          } @else {
            <div class="p-4"><app-durum-kutusu tur="bos" mesaj="Listeden bir hasta seçin." /></div>
          }
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
  protected readonly hub = inject(HubService);
  private readonly islemler = inject(RandevuIslemleri);

  protected readonly bugun = simdiSinyali();
  protected readonly doktorEylemleri = DOKTOR_EYLEMLERI;
  protected readonly etiketler = EYLEM_ETIKETI;
  protected readonly qrAdresi = `${location.origin}/giris?rol=hasta&kimlik=${QR_HASTA_ID}`;

  protected readonly seciliId = signal<string | null>(null);
  protected readonly islemde = signal(false);
  protected readonly hata = signal('');
  protected readonly duyuru = signal('');
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly liste = httpResource<RandevuSatiri[]>(() => '/api/randevular');
  // Seçili satır her yeniden çekmede listeden okunur: durum değişince butonlar da güncellenir.
  protected readonly secili = computed(() =>
    this.liste.hasValue() ? (this.liste.value().find((r) => r.id === this.seciliId()) ?? null) : null,
  );
  // Hasta bandı yalnızca hasta değişince çekilir (her çekim erişim kaydı yazar).
  private readonly seciliHastaId = computed(() => this.secili()?.hastaId);
  protected readonly ozet = httpResource<HastaOzeti>(() => {
    const id = this.seciliHastaId();
    return id ? `/api/hastalar/${id}/ozet` : undefined;
  });

  // Başlıklar CSS uppercase ile büyür: lang="tr" olduğu için "Erişim" → "ERİŞİM" doğru çıkar (SPEC §10.5).
  protected readonly yanBolumler = [
    { baslik: 'Uyarılar', bos: 'Uyarı yok.' },
    { baslik: 'Modele giden veri', bos: 'Henüz AI çağrısı yok.' },
    { baslik: 'Erişim kaydı', bos: 'Sonraki aşamada gösterilecek.' },
  ];

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.liste);
  }

  protected saat(iso: string): string {
    return saatEtiketi(iso, this.bugun());
  }

  protected sec(r: RandevuSatiri): void {
    this.seciliId.set(r.id);
    this.hata.set('');
  }

  protected async uygula(r: RandevuSatiri, eylem: RandevuEylemi): Promise<void> {
    this.islemde.set(true);
    this.hata.set('');
    try {
      await this.islemler.uygula(r.id, eylem);
      this.duyuru.set(`${r.siraNo} ${r.hastaAd}: ${this.etiketler[eylem]} tamam`);
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.islemde.set(false);
      this.liste.reload();
      // Basılan buton pasifleşiyor; odak body'ye düşmesin, eylem çubuğunda kalsın.
      this.kok.nativeElement.querySelector<HTMLElement>('[role=toolbar]')?.focus();
    }
  }
}
