import { afterNextRender, Component, computed, effect, ElementRef, inject, Injector, input, output, signal, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { MultiSelectModule } from 'primeng/multiselect';
import { HekimKararTuru, Icd10Kodu, MuayeneDetayi } from '../cekirdek/api';
import { MuayeneIslemleri } from '../cekirdek/muayene';
import { hataMesaji } from '../cekirdek/randevu';
import { AiKarti, HekimOnayi } from '../ortak/ai-karti';

/** Sunucudaki EnFazlaTani ile aynı; sunucu yine doğrular. */
const EN_FAZLA_TANI = 5;

/**
 * Tanı kodları (SPEC §9.10): onaylı ICD-10 kodları + AI kodlama önerisi (mor/kesik, §10.2). Model yalnızca kapalı
 * listeden kod seçer; Düzenle de aynı listeden seçtirir (serbest kod yazılamaz). Tanıyı hekim koyar.
 */
@Component({
  selector: 'app-tani-karti',
  imports: [AiKarti, ButtonModule, MultiSelectModule, ReactiveFormsModule],
  template: `
    <section class="flex flex-col gap-2" aria-labelledby="tani-baslik">
      <h3 id="tani-baslik" class="font-medium text-slate-900">Tanı kodları (ICD-10)</h3>
      @if (detay().tanilar.length) {
        <ul class="flex flex-col gap-1" data-test="tanilar">
          @for (t of detay().tanilar; track t.kod) {
            <li>
              <span class="font-semibold tabular-nums">{{ t.kod }}</span>
              @if (t.ad) {
                · {{ t.ad }}
              }
            </li>
          }
        </ul>
      } @else {
        <p class="text-slate-700" data-test="tani-yok">Tanı kodu girilmedi.</p>
      }

      @if (detay().taniOnerisi; as o) {
        <p id="tani-karti-baslik" tabindex="-1" class="sr-only">ICD-10 önerisi kartı, durum: {{ durumEtiketi[o.durum] }}</p>
        <app-ai-karti [kaynak]="o.kaynak" [zaman]="o.zaman" [model]="o.model ?? ''" [kotaDoldu]="o.kotaDoldu" [onay]="onay()">
          @if (o.oneriler.length) {
            <ul class="flex flex-col gap-1" [class.line-through]="o.durum === 'Reddedildi'" data-test="tani-onerileri">
              @for (s of o.oneriler; track s.kod) {
                <li>
                  <span class="font-semibold tabular-nums">{{ s.kod }}</span>
                  @if (s.ad) {
                    · {{ s.ad }}
                  }
                  <span class="block text-sm text-slate-700">{{ s.gerekce }}</span>
                </li>
              }
            </ul>
          } @else {
            <p class="text-slate-700" data-test="tani-oneri-yok">Kayıtta dayanak bulunamadı; Düzenle ile listeden seçin.</p>
          }

          @if (duzenleniyor()) {
            <form aiButonlar class="flex w-full flex-col gap-2" (submit)="$event.preventDefault(); karar('duzenle')">
              <span id="tani-secim-etiket" class="font-medium text-slate-900">Kodları kapalı listeden seçin (en fazla {{ enFazla }})</span>
              <p-multiselect
                [formControl]="secim"
                [options]="secenekler()"
                optionLabel="etiket"
                optionValue="kod"
                [filter]="true"
                [selectionLimit]="enFazla"
                [maxSelectedLabels]="0"
                [showToggleAll]="false"
                selectedItemsLabel="{0} kod seçildi"
                filterPlaceHolder="Kod ya da ad ara"
                ariaFilterLabel="Kod ya da ad ara"
                emptyMessage="ICD-10 listesi yüklenemedi"
                filterLocale="en"
                emptyFilterMessage="Listede eşleşen kod yok"
                ariaLabelledBy="tani-secim-etiket"
                placeholder="Kod seçin"
                class="w-full"
                data-test="tani-secim"
              />
              <!-- Seçilenler kutunun dışında, tam adıyla: kutuda taşan chip'ler gizlenirdi (hekim kaydedeceğini görmeli). -->
              @if (secilenler().length) {
                <ul class="flex flex-col gap-1" aria-label="Seçilen kodlar" data-test="tani-secilenler">
                  @for (k of secilenler(); track k.kod) {
                    <li><span class="font-semibold tabular-nums">{{ k.kod }}</span> · {{ k.ad }}</li>
                  }
                </ul>
              } @else {
                <p class="text-slate-700">Kod seçilmedi.</p>
              }
              <div class="flex gap-2">
                <button pButton type="submit" [disabled]="islemde() || !secimVar()">Kaydet ve onayla</button>
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" (click)="vazgec()">Vazgeç</button>
              </div>
            </form>
          } @else if (o.durum === 'Taslak' && !kapali()) {
            <ng-container aiButonlar>
              @if (o.oneriler.length) {
                <button pButton type="button" [disabled]="islemde()" (click)="karar('onayla')">Onayla</button>
              }
              <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="duzenleBasla()">
                Düzenle
              </button>
              @if (reddetSoruluyor()) {
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" id="tani-reddet-onay" [disabled]="islemde()" (click)="karar('reddet')">
                  Evet, reddet
                </button>
                <button pButton type="button" severity="secondary" variant="text" class="ikincil" (click)="reddetVazgec()">Vazgeç</button>
              } @else {
                <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" id="tani-reddet" [disabled]="islemde()" (click)="reddetSor()">
                  Reddet
                </button>
              }
            </ng-container>
          }
        </app-ai-karti>
      }

      @if (!kapali() && (!detay().taniOnerisi || detay().taniOnerisi?.durum === 'Reddedildi')) {
        <div class="flex flex-col items-start gap-1">
          <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="uret()" data-test="tani-uret">
            {{ uretiliyor() ? 'Öneri hazırlanıyor…' : 'ICD-10 önerisi oluştur' }}
          </button>
        </div>
      }
      <p class="text-sm text-slate-700">Kodlama önerisidir; tanıyı hekim koyar. Kodlar kapalı listeden seçilir.</p>

      @if (hata()) {
        <p class="font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }
    </section>
  `,
})
export class TaniKarti {
  private readonly islemler = inject(MuayeneIslemleri);
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly detay = input.required<MuayeneDetayi>();
  /** Öneri ya da karar değişti: üst bileşen muayeneyi, denetim panelini ve erişim kaydını yeniden çeker. */
  readonly degisti = output<void>();

  protected readonly enFazla = EN_FAZLA_TANI;
  protected readonly durumEtiketi: Record<string, string> = { Taslak: 'taslak', Onaylandi: 'onaylandı', Reddedildi: 'reddedildi' };
  protected readonly secim = new FormControl<string[]>([], { nonNullable: true });
  protected readonly secimDegeri = signal<string[]>([]);
  protected readonly liste = signal<Icd10Kodu[]>([]);
  protected readonly duzenleniyor = signal(false);
  protected readonly reddetSoruluyor = signal(false);
  protected readonly islemde = signal(false);
  protected readonly uretiliyor = signal(false);
  protected readonly hata = signal('');

  protected readonly kapali = computed(() => this.detay().durum === 'Kapali');
  protected readonly secimVar = computed(() => this.secimDegeri().length > 0);
  protected readonly secilenler = computed(() =>
    this.secimDegeri().map((kod) => this.liste().find((k) => k.kod === kod) ?? { kod, ad: '' }),
  );
  /** Kart üst bileşen muayeneyi yeniden çekince gelir; odak o zaman verilir (yoksa body'ye düşer). */
  private readonly bekleyenOdak = signal<string | null>(null);
  protected readonly secenekler = computed(() => this.liste().map((k) => ({ kod: k.kod, etiket: `${k.kod} · ${k.ad}` })));
  protected readonly onay = computed<HekimOnayi | null>(() => {
    const k = this.detay().taniOnerisi?.karar;
    if (!k) return null;
    const reddedildi = k.durum === 'Reddedildi';
    // Onaylanan kodlar öneriden farklıysa hekim düzenlemiştir (erişim kaydındaki eylemle aynı ayrım).
    const onerilen = (this.detay().taniOnerisi?.oneriler ?? []).map((s) => s.kod).sort().join();
    const duzenlendi = this.detay().tanilar.map((t) => t.kod).sort().join() !== onerilen;
    const eylem = reddedildi ? 'reddetti' : duzenlendi ? 'düzenleyip onayladı' : 'onayladı';
    return { hekimAd: k.hekimAd, zaman: k.zaman, eylem, reddedildi };
  });

  constructor() {
    this.secim.valueChanges.subscribe((v) => this.secimDegeri.set(v));
    effect(() => {
      const secici = this.bekleyenOdak();
      // Yeni öneri taslak olarak gelince (ret sonrası eski kart hâlâ sayfadayken odaklanılmasın).
      if (!secici || this.detay().taniOnerisi?.durum !== 'Taslak') return;
      untracked(() => {
        this.bekleyenOdak.set(null);
        this.odakla(secici);
      });
    });
  }

  protected async uret(): Promise<void> {
    this.uretiliyor.set(true);
    await this.calistir(async () => {
      await this.islemler.taniOnerisiUret(this.detay().id);
      this.bekleyenOdak.set('#tani-karti-baslik');
    });
    this.uretiliyor.set(false);
  }

  protected async duzenleBasla(): Promise<void> {
    await this.calistir(async () => {
      if (!this.liste().length) this.liste.set(await this.islemler.icd10Listesi());
      this.secim.setValue(this.detay().taniOnerisi?.oneriler.map((s) => s.kod) ?? []);
      this.duzenleniyor.set(true);
      this.odakla('[data-test="tani-secim"] [role="combobox"]');
    }, false);
  }

  protected vazgec(): void {
    this.duzenleniyor.set(false);
    this.odakla('#tani-karti-baslik');
  }

  protected reddetVazgec(): void {
    this.reddetSoruluyor.set(false);
    this.odakla('#tani-reddet');
  }

  protected reddetSor(): void {
    this.reddetSoruluyor.set(true);
    this.odakla('#tani-reddet-onay');
  }

  protected async karar(tur: HekimKararTuru): Promise<void> {
    const o = this.detay().taniOnerisi;
    if (!o) return;
    await this.calistir(async () => {
      await this.islemler.taniKarar(o.id, tur, tur === 'duzenle' ? this.secim.value : null);
      this.duzenleniyor.set(false);
      this.reddetSoruluyor.set(false);
      this.odakla('#tani-karti-baslik');
    });
  }

  private async calistir(is: () => Promise<void>, bildir = true): Promise<void> {
    this.islemde.set(true);
    this.hata.set('');
    try {
      await is();
      if (bildir) this.degisti.emit();
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
