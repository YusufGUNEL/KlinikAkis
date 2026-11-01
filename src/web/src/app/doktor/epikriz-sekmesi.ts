import { afterNextRender, Component, computed, effect, ElementRef, inject, Injector, input, output, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { EpikrizTaslagi, HekimKararTuru, MuayeneDetayi } from '../cekirdek/api';
import { MuayeneIslemleri } from '../cekirdek/muayene';
import { hataMesaji } from '../cekirdek/randevu';
import { AiKarti, HekimOnayi } from '../ortak/ai-karti';
import { TaniKarti } from './tani-karti';

/**
 * Düzenleme kutusuna giden düz metin: yalnızca modelin yazdığı bölümler. Alerji ve tahlil satırları kayıttan/koddan
 * gelir ve düzenlenmez; böylece hekim düzenlerken epikrizden silinemezler.
 */
export function epikrizMetni(t: EpikrizTaslagi): string {
  return [`Öykü: ${t.oyku}`, `Değerlendirme: ${t.degerlendirme}`, `Plan: ${t.plan}`].join('\n');
}

/**
 * Epikriz sekmesi (SPEC §13.7a): doktor notu + AI epikriz taslağı (mor/kesik, §10.2). Model yalnızca öykü,
 * değerlendirme ve plan cümlelerini yazar; alerji ve tahlil satırları sunucuda kodla kurulur ve burada "[kayıttan]",
 * "[işaret kodla hesaplandı]" diye ayrılır. Onayla / Düzenle / Reddet; karardan sonra kenar düz olur.
 */
@Component({
  selector: 'app-epikriz-sekmesi',
  imports: [AiKarti, ButtonModule, TaniKarti],
  template: `
    <div class="flex max-w-3xl flex-col gap-4">
      <form class="flex flex-col gap-2" (submit)="$event.preventDefault(); notKaydet()">
        <label for="hekim-notu" class="font-medium text-slate-900">Doktor notu</label>
        <textarea
          id="hekim-notu"
          rows="3"
          maxlength="2000"
          class="rounded-md border border-slate-500 p-2 disabled:bg-slate-100 disabled:text-slate-600"
          [disabled]="notKilitli()"
          [attr.aria-describedby]="notKilitli() ? 'not-kilit-nedeni' : null"
          [value]="not()"
          (input)="not.set($any($event.target).value); notDurumu.set('')"
        ></textarea>
        <div class="flex items-center gap-3">
          <button pButton type="submit" size="small" severity="secondary" variant="outlined" class="ikincil" [disabled]="notKilitli() || islemde() || !notDegisti()">
            Notu kaydet
          </button>
          @if (!notKilitli()) {
            <span class="text-slate-700" role="status">{{ notDurumu() }}</span>
          }
          @if (kapali()) {
            <span id="not-kilit-nedeni" class="text-slate-700">Vizit kapandı; not değiştirilemez.</span>
          } @else if (epikrizOnayli()) {
            <span id="not-kilit-nedeni" class="text-slate-700" data-test="not-kilitli">Epikriz onaylandı; not değiştirilemez.</span>
          }
        </div>
      </form>

      <app-tani-karti [detay]="detay()" (degisti)="degisti.emit()" />

      @if (detay().epikriz; as e) {
        <p id="epikriz-karti-baslik" tabindex="-1" class="sr-only">Epikriz kartı, durum: {{ durumEtiketi[e.durum] }}</p>
        <app-ai-karti [kaynak]="e.kaynak" [zaman]="e.zaman" [model]="e.model ?? ''" [kotaDoldu]="e.kotaDoldu" [onay]="onay()">
            <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-2" [class.line-through]="e.durum === 'Reddedildi'" data-test="epikriz-bolumleri">
              @if (e.sonMetin) {
                <dt class="text-slate-600">Düzenlenmiş metin</dt>
                <dd class="whitespace-pre-line" data-test="epikriz-son-metin">{{ e.sonMetin }}</dd>
              } @else {
                <dt class="text-slate-600">Öykü</dt>
                <dd>{{ e.taslak.oyku }}</dd>
              }
              <dt class="text-slate-600">Alerji</dt>
              <dd data-test="epikriz-alerji">
                @if (e.taslak.alerji === alerjiYok) {
                  {{ e.taslak.alerji }}
                } @else {
                  <span class="font-semibold text-tehlike"><span aria-hidden="true">⛔ </span>{{ e.taslak.alerji }}</span>
                }
                <span class="ml-1 text-xs text-slate-600">[kayıttan]</span>
              </dd>
              <dt class="text-slate-600">Tahliller</dt>
              <dd>
                @if (e.taslak.tahliller.length) {
                  <ul class="tabular-nums">
                    @for (t of e.taslak.tahliller; track $index) {
                      <li>{{ t }}</li>
                    }
                  </ul>
                } @else {
                  yok
                }
                <span class="text-xs text-slate-600">[işaret kodla hesaplandı]</span>
              </dd>
              @if (!e.sonMetin) {
                <dt class="text-slate-600">Değerlendirme</dt>
                <dd>{{ e.taslak.degerlendirme }}</dd>
                <dt class="text-slate-600">Plan</dt>
                <dd>{{ e.taslak.plan }}</dd>
              }
            </dl>

          @if (duzenleniyor()) {
            <form aiButonlar class="flex w-full flex-col gap-2" (submit)="$event.preventDefault(); karar('duzenle')">
              <label for="epikriz-metni" class="font-medium text-slate-900">Öykü, değerlendirme ve planı düzeltin</label>
              <p class="text-slate-700">Alerji ve tahlil satırları kayıttan gelir, düzenlenmez.</p>
              <textarea
                id="epikriz-metni"
                rows="8"
                maxlength="4000"
                class="rounded-md border border-slate-500 p-2"
                [value]="metin()"
                (input)="metin.set($any($event.target).value)"
              ></textarea>
              <div class="flex gap-2">
                <button pButton type="submit" [disabled]="islemde() || !metin().trim()">Kaydet ve onayla</button>
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" (click)="vazgec()">Vazgeç</button>
              </div>
            </form>
          } @else if (e.durum === 'Taslak' && !kapali()) {
            <ng-container aiButonlar>
              <button pButton type="button" [disabled]="islemde()" (click)="karar('onayla')">Onayla</button>
              <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="duzenleBasla()">
                Düzenle
              </button>
              @if (reddetSoruluyor()) {
                <!-- Ret geri alınamaz: ikinci basışla onaylanır. -->
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" id="epikriz-reddet-onay" [disabled]="islemde()" (click)="karar('reddet')">
                  Evet, reddet
                </button>
                <button pButton type="button" severity="secondary" variant="text" class="ikincil" (click)="reddetSoruluyor.set(false)">Vazgeç</button>
              } @else {
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="reddetSor()">
                  Reddet
                </button>
              }
            </ng-container>
          }
        </app-ai-karti>
      }

      @if (kapali() && detay().epikriz?.durum !== 'Onaylandi') {
        <p class="text-slate-700" data-test="epikriz-kapali">Vizit kapandı; epikriz onaylanmadan kapatıldı.</p>
      } @else if (!detay().epikriz || detay().epikriz?.durum === 'Reddedildi') {
        <div class="flex flex-col items-start gap-2">
          <button pButton type="button" [disabled]="islemde()" (click)="uret()" data-test="epikriz-uret">
            {{ uretiliyor() ? 'Taslak hazırlanıyor…' : detay().epikriz ? 'Yeni epikriz taslağı oluştur' : 'Epikriz taslağı oluştur' }}
          </button>
          <p class="text-slate-700">{{ talimat }}</p>
        </div>
      }

      @if (hata()) {
        <p class="font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }
    </div>
  `,
})
export class EpikrizSekmesi {
  private readonly islemler = inject(MuayeneIslemleri);
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly detay = input.required<MuayeneDetayi>();
  /** Not, taslak ya da karar değişti: üst bileşen muayeneyi, denetim panelini ve erişim kaydını yeniden çeker. */
  readonly degisti = output<void>();

  protected readonly alerjiYok = 'Bilinen alerji yok';
  protected readonly talimat =
    'Taslak; doktor notu, onayladığınız ön anamnez ve tahlillerden hazırlanır. Alerji ve tahlil satırları kayıttan, kodla gelir.';
  protected readonly durumEtiketi: Record<string, string> = { Taslak: 'taslak', Onaylandi: 'onaylandı', Reddedildi: 'reddedildi' };
  protected readonly not = signal('');
  protected readonly notDurumu = signal('');
  protected readonly metin = signal('');
  protected readonly duzenleniyor = signal(false);
  protected readonly reddetSoruluyor = signal(false);
  protected readonly islemde = signal(false);
  protected readonly uretiliyor = signal(false);
  protected readonly hata = signal('');
  private readonly bekleyenOdak = signal(false);

  protected readonly kapali = computed(() => this.detay().durum === 'Kapali');
  // Onaylı epikriz nottan üretildi: not değişirse onaylı metinle 103 paketi birbirini tutmaz (sunucu da 409 verir).
  protected readonly epikrizOnayli = computed(() => this.detay().epikriz?.durum === 'Onaylandi');
  protected readonly notKilitli = computed(() => this.kapali() || this.epikrizOnayli());
  protected readonly notDegisti = computed(() => this.not().trim() !== (this.detay().hekimNotu ?? ''));
  protected readonly onay = computed<HekimOnayi | null>(() => {
    const k = this.detay().epikriz?.karar;
    if (!k) return null;
    const reddedildi = k.durum === 'Reddedildi';
    const eylem = reddedildi ? 'reddetti' : this.detay().epikriz?.sonMetin ? 'düzenleyip onayladı' : 'onayladı';
    return { hekimAd: k.hekimAd, zaman: k.zaman, eylem, reddedildi };
  });

  constructor() {
    // Sunucudaki not değişince (başka muayene seçildi ya da kaydedildi) alan güncellenir; yazılan metin ezilmez.
    let sonMuayene = '';
    effect(() => {
      const d = this.detay();
      untracked(() => {
        if (d.id !== sonMuayene || !this.notDegisti()) this.not.set(d.hekimNotu ?? '');
        sonMuayene = d.id;
      });
    });
    effect(() => {
      if (!this.bekleyenOdak() || this.detay().epikriz?.durum !== 'Taslak') return;
      untracked(() => {
        this.bekleyenOdak.set(false);
        this.odakla('#epikriz-karti-baslik');
      });
    });
  }

  protected async notKaydet(): Promise<void> {
    await this.calistir(async () => {
      await this.islemler.notKaydet(this.detay().id, this.not().trim() || null);
      this.notDurumu.set('✓ Not kaydedildi');
    });
  }

  protected async uret(): Promise<void> {
    this.uretiliyor.set(true);
    await this.calistir(async () => {
      // Kaydedilmemiş not varsa önce kaydedilir: taslak ekrandaki notla üretilsin.
      if (this.notDegisti() && !this.notKilitli()) await this.islemler.notKaydet(this.detay().id, this.not().trim() || null);
      await this.islemler.epikrizUret(this.detay().id);
      // Kart üst bileşen muayeneyi yeniden çekince gelir; odak o zaman verilir (yoksa body'ye düşer).
      this.bekleyenOdak.set(true);
    });
    this.uretiliyor.set(false);
  }

  protected duzenleBasla(): void {
    const e = this.detay().epikriz;
    if (!e) return;
    this.metin.set(epikrizMetni(e.taslak));
    this.duzenleniyor.set(true);
    this.odakla('#epikriz-metni');
  }

  protected vazgec(): void {
    this.duzenleniyor.set(false);
    this.odakla('#epikriz-karti-baslik');
  }

  protected reddetSor(): void {
    this.reddetSoruluyor.set(true);
    this.odakla('#epikriz-reddet-onay');
  }

  protected async karar(tur: HekimKararTuru): Promise<void> {
    const e = this.detay().epikriz;
    if (!e) return;
    await this.calistir(async () => {
      await this.islemler.karar(e.id, tur, tur === 'duzenle' ? this.metin().trim() : null);
      this.duzenleniyor.set(false);
      this.reddetSoruluyor.set(false);
      // Butonlar kalkıyor: odak body'ye düşmesin, kararın yazıldığı karta gitsin.
      this.odakla('#epikriz-karti-baslik');
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

  private odakla(secici: string): void {
    afterNextRender(() => this.kok.nativeElement.querySelector<HTMLElement>(secici)?.focus(), { injector: this.injector });
  }
}
