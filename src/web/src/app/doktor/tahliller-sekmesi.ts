import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { hataMesaji } from '../cekirdek/randevu';
import {
  basamak,
  BilgilendirmeSecimi,
  HASTA_UYARI_METNI,
  ISARET_ETIKETI,
  sayi,
  TahlilIslemleri,
  TahlilSatiri,
  trendMetni,
} from '../cekirdek/tahlil';
import { AiKarti, HekimOnayi } from '../ortak/ai-karti';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { Sparkline } from '../ortak/sparkline';

interface BekleyenYayin {
  satir: TahlilSatiri;
  metin: string;
}

/**
 * Doktor tahlil sekmesi (taslak 5). İşaret (✓, ▲ Y, ‼ KD…) sunucuda kodla hesaplanır; AI yalnızca hastaya açıklama
 * cümlesi taslağı kurar. Her testin son yayınlanmamış sonucu için mor kesik kart: taslak üret → önizle / düzenle /
 * reddet → hastaya yayınla. Kritik değerde yayından önce §10.3 açılır penceresi: bilgilendirme seçimi zorunlu.
 */
@Component({
  selector: 'app-tahliller-sekmesi',
  imports: [AiKarti, ButtonModule, DatePipe, DecimalPipe, DialogModule, DurumKutusu, Sparkline],
  template: `
    @if (satirlar().length === 0) {
      <app-durum-kutusu tur="bos" mesaj="Bu hastanın tahlil sonucu yok." />
    } @else {
      <table class="w-full max-w-4xl border-collapse text-[13px]" data-test="tahlil-tablosu">
        <caption class="sr-only">Tahlil sonuçları; işaret kodla hesaplanır</caption>
        <thead>
          <tr class="h-9 border-b border-slate-300 text-left text-xs text-slate-600">
            <th scope="col" class="pr-3 font-semibold">Test</th>
            <th scope="col" class="pr-3 text-right font-semibold">Sonuç</th>
            <th scope="col" class="pr-3 font-semibold">Birim</th>
            <th scope="col" class="pr-3 font-semibold">Referans</th>
            <th scope="col" class="pr-3 font-semibold">Durum</th>
            <th scope="col" class="pr-3 font-semibold">Trend</th>
            <th scope="col" class="pr-3 font-semibold">Alındı</th>
            <th scope="col" class="font-semibold">LOINC</th>
          </tr>
        </thead>
        <tbody>
          @for (s of satirlar(); track s.tahlilTanimiId) {
            @let e = etiket[s.son.isaret];
            <tr class="h-9 border-b border-slate-100">
              <th scope="row" class="pr-3 text-left font-medium text-slate-900">{{ s.ad }}</th>
              <td class="pr-3 text-right tabular-nums" [class.font-semibold]="s.son.isaret !== 'Normal'">
                {{ s.son.deger | number: basamak(s.ondalik) }}
              </td>
              <td class="pr-3 text-slate-700">{{ s.birim }}</td>
              <td class="pr-3 tabular-nums text-slate-700">
                {{ s.altSinir | number: basamak(s.ondalik) }}–{{ s.ustSinir | number: basamak(s.ondalik) }}
              </td>
              <td class="pr-3 font-semibold" [class]="e.renk" data-test="isaret">
                <span aria-hidden="true">{{ e.doktor }}</span><span class="sr-only">{{ e.okunus }}</span>
              </td>
              <td class="pr-3">
                <app-sparkline [degerler]="degerler(s)" [etiket]="trend(s)" />
              </td>
              <td class="pr-3 tabular-nums text-slate-700">{{ s.son.zaman | date: 'dd.MM.yyyy HH:mm' }}</td>
              <td class="tabular-nums text-slate-600">{{ s.loincKodu }}</td>
            </tr>
          }
        </tbody>
      </table>
      <p class="mt-2 text-xs text-slate-600">
        Durum kodla hesaplanır (cinsiyete göre aralık, kritik eşikler); AI yalnızca hastaya açıklama cümlesi kurar.
      </p>

      <h3 class="mt-5 text-xs font-semibold tracking-wide text-slate-600 uppercase">Hasta açıklamaları</h3>
      <div class="mt-2 flex max-w-3xl flex-col gap-3">
        @for (s of satirlar(); track s.tahlilTanimiId) {
          @let a = s.aciklama;
          @if (s.son.hastayaYayinlandi && a.yayinlanan) {
            <app-ai-karti [kaynak]="a.kaynak ?? 'Kural'" [zaman]="a.zaman ?? a.yayinZamani!" [model]="a.model ?? ''" [onay]="onay(s)">
              <p class="font-medium">{{ s.hastaDostuAd }} ({{ s.ad }}) · {{ s.son.deger | number: basamak(s.ondalik) }} {{ s.birim }}</p>
              <p class="mt-1 whitespace-pre-line" data-test="yayinlanan">{{ a.yayinlanan }}</p>
              @if (a.taslak && a.taslak !== a.yayinlanan) {
                <p class="mt-2 text-xs text-slate-600">Hekim düzenledi · makine taslağı: <span class="line-through">{{ a.taslak }}</span></p>
              }
            </app-ai-karti>
          } @else if (!s.son.hastayaYayinlandi) {
            @if (a.taslak && !reddedilen().has(s.son.id)) {
              <app-ai-karti [kaynak]="a.kaynak ?? 'Kural'" [zaman]="a.zaman!" [model]="a.model ?? ''" [kotaDoldu]="a.kotaDoldu">
                <p class="font-medium">
                  {{ s.hastaDostuAd }} ({{ s.ad }}) · {{ s.son.deger | number: basamak(s.ondalik) }} {{ s.birim }}
                  <span [class]="etiket[s.son.isaret].renk">{{ etiket[s.son.isaret].doktor }}</span>
                </p>
                @if (duzenlenen() !== s.son.id) {
                  <p class="mt-1 whitespace-pre-line" data-test="taslak">{{ a.taslak }}</p>
                  <p class="mt-2 text-xs text-slate-600">Hastada altına eklenir: “{{ uyariMetni }}”</p>
                }
                @if (duzenlenen() === s.son.id) {
                  <form aiButonlar class="flex w-full flex-col gap-2" (submit)="$event.preventDefault(); yayinla(s, metin())">
                    <label [attr.for]="'aciklama-' + s.son.id" class="font-medium text-slate-900">Hastaya gidecek açıklama</label>
                    <textarea
                      [id]="'aciklama-' + s.son.id"
                      rows="4"
                      maxlength="1000"
                      class="rounded-md border border-slate-500 p-2"
                      [value]="metin()"
                      (input)="metin.set($any($event.target).value)"
                    ></textarea>
                    <div class="flex gap-2">
                      <button pButton type="submit" [disabled]="islemde() || !metin().trim()">Düzenlenmiş hâliyle yayınla</button>
                      <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" (click)="duzenlenen.set(null)">
                        Vazgeç
                      </button>
                    </div>
                  </form>
                } @else {
                  <ng-container aiButonlar>
                    <button pButton type="button" [disabled]="islemde()" (click)="yayinla(s, a.taslak)">Hastaya yayınla</button>
                    <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="duzenle(s)">
                      Düzenle
                    </button>
                    <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="reddet(s)">
                      Reddet
                    </button>
                  </ng-container>
                }
              </app-ai-karti>
            } @else {
              <div class="flex flex-wrap items-center gap-3 rounded-lg border border-slate-200 bg-white p-3">
                <span class="font-medium">{{ s.hastaDostuAd }} ({{ s.ad }})</span>
                @if (reddedilen().has(s.son.id)) {
                  <span class="text-slate-700">Taslak reddedildi; hastaya yayınlanmayacak.</span>
                  <button pButton type="button" size="small" severity="secondary" variant="outlined" class="ikincil" (click)="geriAl(s)">Geri al</button>
                } @else {
                  <span class="text-slate-700">Hastaya yayınlanmadı.</span>
                }
                <button pButton type="button" size="small" [disabled]="islemde()" (click)="taslakUret(s)" [attr.data-test]="'taslak-uret-' + s.loincKodu">
                  {{ uretilen() === s.son.id ? 'Hazırlanıyor…' : 'AI açıklama taslağı üret' }}
                </button>
              </div>
            }
          }
        }
      </div>
      <p class="sr-only" role="status">{{ duyuru() }}</p>
      @if (hata()) {
        <p class="mt-2 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }
    }

    <p-dialog
      [visible]="!!bekleyen()"
      (visibleChange)="!$event && kapat()"
      [modal]="true"
      [closable]="true"
      closeAriaLabel="Kapat"
      header="Kritik değer hastaya yayınlanıyor"
      [style]="{ width: '30rem' }"
    >
      @if (bekleyen(); as b) {
        <p class="font-semibold text-tehlike" data-test="kritik-deger">
          <span aria-hidden="true">‼</span> {{ b.satir.ad }} {{ sayi(b.satir.son.deger, b.satir.ondalik) }} {{ b.satir.birim }} ·
          {{ etiket[b.satir.son.isaret].okunus }}
        </p>
        <fieldset class="mt-4">
          <legend class="font-medium text-slate-900">Hasta bilgilendirmesi (zorunlu, erişim kaydına yazılır)</legend>
          @for (o of secenekler; track o.deger) {
            <label class="mt-2 flex min-h-10 cursor-pointer items-center gap-2">
              <input
                type="radio"
                name="bilgilendirme"
                class="size-4 accent-ana"
                [value]="o.deger"
                [checked]="secim() === o.deger"
                (change)="secim.set(o.deger)"
              />
              {{ o.etiket }}
            </label>
          }
        </fieldset>
        @if (hata()) {
          <p class="mt-2 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
        }
      }
      <ng-template #footer>
        <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" (click)="kapat()">Vazgeç</button>
        <button pButton type="button" [disabled]="!secim() || islemde()" (click)="onayla()" data-test="kritik-yayinla">Yayınla</button>
      </ng-template>
    </p-dialog>
  `,
})
export class TahlillerSekmesi {
  private readonly islemler = inject(TahlilIslemleri);

  readonly satirlar = input.required<TahlilSatiri[]>();
  /** Taslak üretildi ya da yayınlandı: üst bileşen tahlilleri, erişim kaydını ve denetim panelini yeniden çeker. */
  readonly degisti = output<void>();

  protected readonly etiket = ISARET_ETIKETI;
  protected readonly basamak = basamak;
  protected readonly sayi = sayi;
  protected readonly uyariMetni = HASTA_UYARI_METNI;
  protected readonly secenekler: { deger: BilgilendirmeSecimi; etiket: string }[] = [
    { deger: 'TelefonlaBilgilendirildi', etiket: 'Hasta telefonla bilgilendirildi' },
    { deger: 'Bilgilendirilecek', etiket: 'Bilgilendirilecek' },
  ];

  protected readonly islemde = signal(false);
  protected readonly uretilen = signal<string | null>(null);
  protected readonly duzenlenen = signal<string | null>(null);
  protected readonly metin = signal('');
  /** Reddedilen taslaklar yalnızca bu ekranda saklanır; sunucuya yazılmaz, yayınlanmaz. */
  protected readonly reddedilen = signal<ReadonlySet<string>>(new Set());
  protected readonly bekleyen = signal<BekleyenYayin | null>(null);
  protected readonly secim = signal<BilgilendirmeSecimi | null>(null);
  protected readonly hata = signal('');
  protected readonly duyuru = signal('');

  protected degerler(s: TahlilSatiri): number[] {
    return s.trend.map((t) => t.deger);
  }

  protected trend(s: TahlilSatiri): string {
    return trendMetni(this.degerler(s), s.ondalik);
  }

  protected onay(s: TahlilSatiri): HekimOnayi | null {
    const a = s.aciklama;
    return a.yayinZamani ? { hekimAd: a.yayinlayanHekim ?? 'Hekim', zaman: a.yayinZamani, eylem: 'hastaya yayınladı' } : null;
  }

  protected async taslakUret(s: TahlilSatiri): Promise<void> {
    this.reddedilen.update((r) => new Set([...r].filter((id) => id !== s.son.id)));
    await this.calistir(async () => {
      this.uretilen.set(s.son.id);
      try {
        await this.islemler.aciklama(s.son.id);
      } finally {
        this.uretilen.set(null);
      }
      this.duyuru.set(`${s.ad} için açıklama taslağı hazır.`);
    });
  }

  protected duzenle(s: TahlilSatiri): void {
    this.metin.set(s.aciklama.taslak ?? '');
    this.duzenlenen.set(s.son.id);
  }

  protected reddet(s: TahlilSatiri): void {
    this.reddedilen.update((r) => new Set([...r, s.son.id]));
    this.duyuru.set(`${s.ad} taslağı reddedildi.`);
  }

  protected geriAl(s: TahlilSatiri): void {
    this.reddedilen.update((r) => new Set([...r].filter((id) => id !== s.son.id)));
  }

  /** Kritik değerde önce açılır pencere (§10.3); diğerlerinde doğrudan yayın. */
  protected yayinla(s: TahlilSatiri, metin: string): void {
    if (ISARET_ETIKETI[s.son.isaret].kritik) {
      this.secim.set(null);
      this.bekleyen.set({ satir: s, metin: metin.trim() });
      return;
    }
    void this.gonder(s, metin.trim(), null);
  }

  protected kapat(): void {
    this.bekleyen.set(null);
    this.secim.set(null);
  }

  protected async onayla(): Promise<void> {
    const b = this.bekleyen();
    const secim = this.secim();
    if (!b || !secim) return;
    await this.gonder(b.satir, b.metin, secim);
    if (!this.hata()) this.kapat();
  }

  private async gonder(s: TahlilSatiri, metin: string, bilgilendirme: BilgilendirmeSecimi | null): Promise<void> {
    await this.calistir(async () => {
      await this.islemler.yayinla(s.son.id, metin, bilgilendirme);
      this.duzenlenen.set(null);
      this.duyuru.set(`${s.ad} hastaya yayınlandı.`);
    });
  }

  private async calistir(is: () => Promise<void>): Promise<void> {
    this.islemde.set(true);
    this.hata.set('');
    try {
      await is();
      this.degisti.emit();
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.islemde.set(false);
    }
  }
}
