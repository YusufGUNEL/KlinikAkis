import { afterNextRender, Component, ElementRef, inject, Injector, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { HastaOzetKarti, KirmiziBayrak, SohbetSorusu } from '../cekirdek/api';
import { OnAnamnezIslemleri } from '../cekirdek/onanamnez';
import { hataMesaji } from '../cekirdek/randevu';
import { AiKarti } from '../ortak/ai-karti';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { Ikon } from '../ortak/ikon';

interface GecmisAdim {
  soru: string;
  cevap: string;
}

/** Sohbet adımlarının kısa adları (soru metinleri kartta uzun kalıyor). */
const ADIM_ETIKETI: Record<number, string> = {
  1: 'Şikayet',
  2: 'Süre',
  3: 'Şiddet',
  4: 'Ateş',
  5: 'Diğer belirtiler',
  6: 'Ek bilgi',
};

type Asama = 'basliyor' | 'sohbet' | 'ozet' | 'onay';
/** Hastanın özete cevabı (taslak 2). */
type HastaCevabi = 'bekliyor' | 'duzeltiyor' | 'dogru' | 'notlu';

/**
 * Şikayet sohbeti (SPEC §10.4 taslak 1, §9.4). Sorular sunucudan gelir; her cevapta sunucu kırmızı bayrak
 * kuralını çalıştırır. Bayrak gelirse 112 bandı çıkar ve akış durur (randevu yok).
 * Sohbet bitince özet ve bölüm önerisi mor kesik AI kartında gelir (taslak 2); hasta "Doğru" der ya da not ekler,
 * sonra önerilen (ya da alternatif) bölüm ön seçili randevu ekranına geçer. Bölüm listesi ve doğrulama sunucuda.
 */
@Component({
  selector: 'app-sikayet',
  imports: [AiKarti, ButtonModule, DurumKutusu, Ikon, RouterLink],
  template: `
    <a routerLink=".." class="inline-flex min-h-12 items-center text-base font-medium text-ana">← Randevularım</a>
    <h1 class="mt-2 text-2xl font-semibold text-slate-900">Şikayetiniz</h1>

    @if (asama() !== 'basliyor') {
      <div class="mt-3">
        <p class="text-base text-slate-700">Adım {{ adim() }}/{{ toplam() }}</p>
        <div
          class="mt-1 h-2 overflow-hidden rounded-full bg-slate-200"
          role="progressbar"
          aria-label="Sohbet ilerlemesi"
          [attr.aria-valuenow]="adim()"
          aria-valuemin="1"
          [attr.aria-valuemax]="toplam()"
          [attr.aria-valuetext]="'Adım ' + adim() + ' / ' + toplam()"
        >
          <div class="h-full bg-ana transition-[width]" [style.width.%]="(100 * adim()) / toplam()"></div>
        </div>
      </div>
    }

    @if (gecmis().length) {
    <ol class="mt-4 flex flex-col gap-3" aria-label="Sohbet">
      @for (g of gecmis(); track $index) {
        <li class="flex flex-col gap-2">
          <p class="text-slate-900"><app-ikon adi="doktor" /> {{ g.soru }}</p>
          <p class="max-w-[85%] self-end rounded-lg bg-ana/10 px-3 py-2 text-slate-900 [overflow-wrap:anywhere]">
            <span class="sr-only">Cevabınız: </span>{{ g.cevap }}
          </p>
        </li>
      }
    </ol>
    }

    @if (bayrak(); as b) {
      <div id="kirmizi-bayrak" tabindex="-1" class="mt-4 outline-none rounded-lg bg-tehlike p-4 text-white" role="alert">
        <p class="text-lg font-semibold"><app-ikon adi="uyari" /> {{ b.mesaj }}</p>
        <a
          [attr.href]="acilArama"
          class="mt-3 flex min-h-12 items-center justify-center rounded-lg bg-white text-lg font-semibold text-tehlike focus-visible:outline-white"
        >
          <app-ikon adi="uyari" /> 112'yi arayın
        </a>
        <p class="mt-3 text-base">Bu belirtilerle randevu beklemeyin. Randevu adımı durduruldu.</p>
      </div>
    } @else if (asama() === 'basliyor') {
      <app-durum-kutusu class="mt-4 block" [tur]="hata() ? 'hata' : 'yukleniyor'" [mesaj]="hata()" (yenidenDene)="baslat()" />
    } @else if (asama() === 'sohbet' && soru(); as s) {
      <section class="mt-4" aria-labelledby="guncel-soru" [attr.aria-busy]="gonderiliyor()">
        <h2 id="guncel-soru" tabindex="-1" class="font-semibold outline-none text-slate-900">
          <app-ikon adi="doktor" /> {{ s.metin }}
        </h2>

        @if (s.secenekler.length) {
          <div class="mt-3 grid grid-cols-2 gap-2" [attr.role]="s.cokluSecim ? 'group' : null" [attr.aria-label]="s.cokluSecim ? 'Belirtiler' : null">
            @for (o of s.secenekler; track o) {
              @if (s.cokluSecim) {
                <button
                  type="button"
                  class="min-h-12 rounded-lg border px-3 py-2 text-left text-base"
                  [class]="secimler().includes(o) ? 'border-ana bg-ana/10 font-semibold text-slate-900' : 'border-slate-500 text-slate-900'"
                  [attr.aria-pressed]="secimler().includes(o)"
                  [disabled]="gonderiliyor()"
                  (click)="secimDegistir(o)"
                >
                  <span aria-hidden="true">{{ secimler().includes(o) ? '☑' : '☐' }} </span>{{ o }}
                </button>
              } @else {
                <button
                  type="button"
                  class="min-h-12 rounded-lg border border-slate-500 px-3 py-2 text-left text-base text-slate-900 hover:border-ana hover:bg-slate-50"
                  [disabled]="gonderiliyor()"
                  (click)="gonder([o])"
                >
                  {{ o }}
                </button>
              }
            }
          </div>
        }

        @if (s.cokluSecim) {
          <button pButton type="button" class="mt-3 min-h-12 w-full justify-center text-lg" [disabled]="gonderiliyor() || !gonderilebilir()" (click)="metniGonder()">
            Devam
          </button>
        }

        <form class="mt-3 flex gap-2" (submit)="$event.preventDefault(); metniGonder()">
          <label for="cevap-metni" class="sr-only">Cevabınızı yazın</label>
          <input
            id="cevap-metni"
            name="metin"
            class="min-h-12 flex-1 rounded-lg border border-slate-500 px-3 text-base"
            placeholder="Yazın…"
            maxlength="1000"
            autocomplete="off"
            [value]="metin()"
            (input)="metin.set($any($event.target).value)"
            [disabled]="gonderiliyor()"
          />
          <!-- Çoklu seçimde tek düğme "Devam": seçim + yazı birlikte gider (Enter yine gönderir). -->
          @if (!s.cokluSecim) {
            <button pButton type="submit" class="min-h-12 min-w-12" aria-label="Gönder" [disabled]="gonderiliyor() || !gonderilebilir()">
              <app-ikon adi="cagrildi" />
            </button>
          }
        </form>

        @if (s.atlanabilir) {
          <button type="button" class="mt-2 min-h-12 w-full rounded-lg border border-slate-500 text-base text-slate-700" [disabled]="gonderiliyor()" (click)="gonder([])">
            Eklemek istediğim yok, bitir
          </button>
        }

        <p class="mt-2 min-h-6 text-base text-slate-700" role="status">{{ gonderiliyor() ? 'Gönderiliyor…' : '' }}</p>

        @if (hata()) {
          <p class="mt-3 font-medium text-uyari" role="alert"><app-ikon adi="uyari" /> {{ hata() }}</p>
        }
      </section>
    } @else if (asama() === 'ozet') {
      <app-durum-kutusu class="mt-4 block" [tur]="hata() ? 'hata' : 'yukleniyor'" [mesaj]="hata() || 'Cevaplarınız doktorunuz için hazırlanıyor…'" (yenidenDene)="ozetle()" />
    } @else if (asama() === 'onay' && kart(); as k) {
      <section class="mt-4" aria-labelledby="ozet-baslik">
        <h2 id="ozet-baslik" tabindex="-1" class="text-lg font-semibold outline-none text-slate-900">Doktorunuza gidecek özet</h2>
        <app-ai-karti class="mt-2 block text-[17px]" [zaman]="k.zaman" hedef="hasta">
          <p class="text-base text-slate-700">Cevaplarınız doktorunuza iletilir; doktorunuz muayenede değerlendirir.</p>
          <!-- Hasta yalnızca kendi cevaplarını görür; modelin yazdığı metin hekim onayı olmadan hastaya gösterilmez. -->
          <dl class="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1" data-test="cevaplar">
            @for (c of k.cevaplar; track c.adim) {
              <dt class="text-slate-600">{{ adimEtiketi[c.adim] ?? c.soru }}</dt>
              <dd>{{ c.cevap }}</dd>
            }
          </dl>
          <p class="mt-3">Önerilen bölüm: <strong data-test="onerilen">{{ k.onerilen.ad }}</strong></p>
          @if (k.alternatif; as alt) {
            <p class="mt-1 text-slate-700">Alternatif: {{ alt.ad }}</p>
          }
          <div aiButonlar class="w-full">
            @switch (cevap()) {
              @case ('bekliyor') {
                <div class="grid grid-cols-2 gap-2">
                  <button pButton type="button" severity="secondary" variant="outlined" id="duzelt-dugmesi" class="ikincil min-h-12 justify-center text-lg" (click)="duzeltBasla()">
                    Düzelt
                  </button>
                  <button pButton type="button" class="min-h-12 justify-center text-lg" [disabled]="gonderiliyor()" (click)="onayla(true)">
                    Doğru <span aria-hidden="true">✓</span>
                  </button>
                </div>
              }
              @case ('duzeltiyor') {
                <form class="flex flex-col gap-2" (submit)="$event.preventDefault(); onayla(false)">
                  <label for="hasta-notu" class="text-base text-slate-900">Neyin yanlış ya da eksik olduğunu yazın</label>
                  <textarea
                    id="hasta-notu"
                    rows="3"
                    maxlength="1000"
                    class="rounded-lg border border-slate-500 p-3 text-base"
                    [value]="not()"
                    (input)="not.set($any($event.target).value)"
                  ></textarea>
                  <button pButton type="submit" class="min-h-12 justify-center text-lg" [disabled]="gonderiliyor() || !not().trim()">
                    Notu doktoruma ilet
                  </button>
                  <button pButton type="button" severity="secondary" variant="outlined" class="ikincil min-h-12 justify-center text-lg" (click)="vazgec()">
                    Vazgeç
                  </button>
                </form>
              }
              @case ('dogru') {
                <p id="hasta-cevabi" tabindex="-1" class="font-medium text-basari outline-none" role="status"><span aria-hidden="true">✓ </span>Özeti doğruladınız.</p>
              }
              @case ('notlu') {
                <p id="hasta-cevabi" tabindex="-1" class="font-medium text-basari outline-none" role="status"><span aria-hidden="true">✓ </span>Notunuz doktorunuza iletilecek.</p>
              }
            }
          </div>
        </app-ai-karti>

        @if (hata()) {
          <p class="mt-3 font-medium text-uyari" role="alert"><app-ikon adi="uyari" /> {{ hata() }}</p>
        }

        @if (cevap() === 'dogru' || cevap() === 'notlu') {
          <a
            pButton
            routerLink="../randevu-al"
            [queryParams]="{ onAnamnez: id, brans: k.onerilen.bransId }"
            class="mt-4 min-h-12 w-full justify-center text-lg"
            data-test="randevu-onerilen"
          >
            {{ k.onerilen.ad }} için saat seçin
          </a>
          @if (k.alternatif; as alt) {
            <a
              pButton
              severity="secondary"
              variant="outlined"
              routerLink="../randevu-al"
              [queryParams]="{ onAnamnez: id, brans: alt.bransId }"
              class="ikincil mt-2 min-h-12 w-full justify-center text-lg"
            >
              {{ alt.ad }} için saat seçin
            </a>
          }
        } @else {
          <p class="mt-3 text-base text-slate-700">Randevuya geçmeden önce özeti kontrol edin.</p>
        }
      </section>
    }

    @if (!bayrak()) {
      <p class="sticky bottom-0 -mx-4 mt-6 border-t border-slate-200 bg-white px-4 py-1 text-base text-slate-700">
        <app-ikon adi="uyari" /> Göğüs ağrısı, nefes darlığı ya da bilinç kaybı varsa beklemeyin:
        <a [attr.href]="acilArama" class="inline-flex min-h-12 items-center font-semibold text-slate-900" [class.text-ana]="acilArama" [class.underline]="acilArama">112'yi arayın</a>
      </p>
    }
  `,
})
export class Sikayet {
  private readonly islemler = inject(OnAnamnezIslemleri);
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /**
   * 112 alanı yerinde duruyor ama şimdilik bağlantısız: demo sırasında ekrana yanlışlıkla dokunulunca
   * telefon gerçekten 112'yi aramasın. Gerçek kullanımda 'tel:112' yapılır (SPEC §9.3).
   */
  protected readonly acilArama: string | null = null;

  protected id = '';
  protected readonly asama = signal<Asama>('basliyor');
  protected readonly soru = signal<SohbetSorusu | null>(null);
  protected readonly adim = signal(1);
  protected readonly toplam = signal(6);
  protected readonly gecmis = signal<GecmisAdim[]>([]);
  protected readonly bayrak = signal<KirmiziBayrak | null>(null);
  protected readonly secimler = signal<string[]>([]);
  protected readonly gonderiliyor = signal(false);
  protected readonly hata = signal('');
  protected readonly metin = signal('');
  protected readonly kart = signal<HastaOzetKarti | null>(null);
  protected readonly adimEtiketi = ADIM_ETIKETI;
  protected readonly cevap = signal<HastaCevabi>('bekliyor');
  protected readonly not = signal('');

  constructor() {
    void this.baslat();
  }

  protected async baslat(): Promise<void> {
    this.hata.set('');
    try {
      const y = await this.islemler.baslat();
      this.id = y.onAnamnezId;
      this.toplam.set(y.toplamAdim);
      this.soruyaGec(y.ilkSoru);
      this.asama.set('sohbet');
    } catch (e) {
      this.hata.set(hataMesaji(e));
    }
  }

  protected gonderilebilir(): boolean {
    return this.metin().trim().length > 0 || (!!this.soru()?.cokluSecim && this.secimler().length > 0);
  }

  protected secimDegistir(o: string): void {
    this.secimler.update((s) => {
      if (s.includes(o)) return s.filter((x) => x !== o);
      // "Hiçbiri" diğerleriyle birlikte seçilemez (sunucu da reddeder).
      return o === 'Hiçbiri' ? [o] : [...s.filter((x) => x !== 'Hiçbiri'), o];
    });
  }

  protected metniGonder(): void {
    if (!this.gonderilebilir()) return;
    void this.gonder(this.soru()?.cokluSecim ? this.secimler() : []);
  }

  protected async gonder(secimler: string[]): Promise<void> {
    const s = this.soru();
    if (!s || this.gonderiliyor()) return;
    const metin = this.metin().trim() || null;
    this.gonderiliyor.set(true);
    this.hata.set('');
    try {
      const y = await this.islemler.cevapla(this.id, secimler, metin);
      const cevap = [...secimler, metin].filter((x) => !!x).join(', ');
      this.gecmis.update((g) => [...g, { soru: s.metin, cevap: cevap || 'Eklemek istediğim yok' }]);
      this.metin.set('');
      this.secimler.set([]);
      if (y.kirmiziBayrak) {
        this.bayrakGoster(y.kirmiziBayrak);
      } else if (y.sonrakiSoru) {
        this.soruyaGec(y.sonrakiSoru);
      } else {
        await this.ozetle();
      }
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.gonderiliyor.set(false);
    }
  }

  protected async ozetle(): Promise<void> {
    this.asama.set('ozet');
    this.hata.set('');
    try {
      const y = await this.islemler.ozet(this.id);
      if (y.kirmiziBayrak) this.bayrakGoster(y.kirmiziBayrak);
      else {
        this.kart.set(y.kart);
        this.asama.set('onay');
        // Başlık ekranın üstüne gelsin: kart alttaki 112 bandının arkasında kalmasın.
        this.odakla('#ozet-baslik', true);
      }
    } catch (e) {
      this.hata.set(hataMesaji(e));
    }
  }

  protected duzeltBasla(): void {
    this.cevap.set('duzeltiyor');
    // Form ekranın üstüne gelsin: "Notu doktoruma ilet" alttaki yapışkan 112 bandının arkasında kalmasın.
    this.odakla('#hasta-notu', true);
  }

  protected vazgec(): void {
    this.cevap.set('bekliyor');
    this.not.set('');
    // Odak kaybolmasın: form kapanınca Düzelt düğmesine döner.
    this.odakla('#duzelt-dugmesi');
  }

  /** Doğru: özet onaylanır. Düzelt: not hekime gider; taslağın kendisi değişmez. */
  protected async onayla(dogru: boolean): Promise<void> {
    const not = dogru ? null : this.not().trim();
    if (!dogru && !not) return;
    this.gonderiliyor.set(true);
    this.hata.set('');
    try {
      await this.islemler.hastaOnayi(this.id, dogru, not);
      this.cevap.set(dogru ? 'dogru' : 'notlu');
      this.odakla('#hasta-cevabi');
    } catch (e) {
      this.hata.set(hataMesaji(e));
    } finally {
      this.gonderiliyor.set(false);
    }
  }

  /** Form kalkıyor: klavye odağı body'ye düşmesin, banda gitsin. */
  private bayrakGoster(b: KirmiziBayrak): void {
    this.bayrak.set(b);
    this.soru.set(null);
    this.odakla('#kirmizi-bayrak');
  }

  private soruyaGec(s: SohbetSorusu): void {
    this.soru.set(s);
    this.adim.set(s.adim);
    this.odakla('#guncel-soru');
  }

  /** Yeni soru ekran okuyucuda okunsun ve telefonda görünür alana kaysın. */
  private odakla(secici: string, basa = false): void {
    afterNextRender(
      () => {
        const el = this.kok.nativeElement.querySelector<HTMLElement>(secici);
        el?.focus({ preventScroll: basa });
        if (basa) el?.scrollIntoView({ block: 'start' });
      },
      { injector: this.injector },
    );
  }
}
