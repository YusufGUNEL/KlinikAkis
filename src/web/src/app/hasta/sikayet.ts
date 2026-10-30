import { afterNextRender, Component, ElementRef, inject, Injector, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { KirmiziBayrak, SohbetSorusu } from '../cekirdek/api';
import { OnAnamnezIslemleri } from '../cekirdek/onanamnez';
import { hataMesaji } from '../cekirdek/randevu';
import { DurumKutusu } from '../ortak/durum-kutusu';

interface GecmisAdim {
  soru: string;
  cevap: string;
}

type Asama = 'basliyor' | 'sohbet' | 'ozet' | 'bitti';

/**
 * Şikayet sohbeti (SPEC §10.4 taslak 1, §9.4). Sorular sunucudan gelir; her cevapta sunucu kırmızı bayrak
 * kuralını çalıştırır. Bayrak gelirse 112 bandı çıkar ve akış durur (randevu yok).
 */
@Component({
  selector: 'app-sikayet',
  imports: [ButtonModule, DurumKutusu, RouterLink],
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
          <p class="text-slate-900"><span aria-hidden="true">🩺 </span>{{ g.soru }}</p>
          <p class="max-w-[85%] self-end rounded-lg bg-ana/10 px-3 py-2 text-slate-900 [overflow-wrap:anywhere]">
            <span class="sr-only">Cevabınız: </span>{{ g.cevap }}
          </p>
        </li>
      }
    </ol>
    }

    @if (bayrak(); as b) {
      <div id="kirmizi-bayrak" tabindex="-1" class="mt-4 outline-none rounded-lg bg-tehlike p-4 text-white" role="alert">
        <p class="text-lg font-semibold"><span aria-hidden="true">⚠ </span>{{ b.mesaj }}</p>
        <a
          href="tel:112"
          class="mt-3 flex min-h-12 items-center justify-center rounded-lg bg-white text-lg font-semibold text-tehlike focus-visible:outline-white"
        >
          <span aria-hidden="true">📞 </span>112'yi arayın
        </a>
        <p class="mt-3 text-base">Bu belirtilerle randevu beklemeyin. Randevu adımı durduruldu.</p>
      </div>
    } @else if (asama() === 'basliyor') {
      <app-durum-kutusu class="mt-4 block" [tur]="hata() ? 'hata' : 'yukleniyor'" [mesaj]="hata()" (yenidenDene)="baslat()" />
    } @else if (asama() === 'sohbet' && soru(); as s) {
      <section class="mt-4" aria-labelledby="guncel-soru" [attr.aria-busy]="gonderiliyor()">
        <h2 id="guncel-soru" tabindex="-1" class="font-semibold outline-none text-slate-900">
          <span aria-hidden="true">🩺 </span>{{ s.metin }}
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
          <button pButton type="submit" class="min-h-12 min-w-12" aria-label="Gönder" [disabled]="gonderiliyor() || !gonderilebilir()">
            <span aria-hidden="true">➤</span>
          </button>
        </form>

        @if (s.atlanabilir) {
          <button type="button" class="mt-2 min-h-12 w-full rounded-lg border border-slate-500 text-base text-slate-700" [disabled]="gonderiliyor()" (click)="gonder([])">
            Eklemek istediğim yok, bitir
          </button>
        }

        <p class="mt-2 min-h-6 text-base text-slate-700" role="status">{{ gonderiliyor() ? 'Gönderiliyor…' : '' }}</p>

        @if (hata()) {
          <p class="mt-3 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
        }
      </section>
    } @else if (asama() === 'ozet') {
      <app-durum-kutusu class="mt-4 block" [tur]="hata() ? 'hata' : 'yukleniyor'" [mesaj]="hata() || 'Cevaplarınız doktorunuz için hazırlanıyor…'" (yenidenDene)="ozetle()" />
    } @else if (asama() === 'bitti') {
      <section class="mt-4 rounded-lg border border-slate-200 p-4" aria-labelledby="bitti-baslik">
        <h2 id="bitti-baslik" tabindex="-1" class="font-semibold outline-none text-slate-900">
          <span class="text-basari" aria-hidden="true">✓ </span>Teşekkürler, cevaplarınız doktorunuza iletilecek.
        </h2>
        <p class="mt-1 text-[17px] text-slate-700">Şimdi bölüm ve saat seçerek randevunuzu alın.</p>
        <a pButton routerLink="../randevu-al" [queryParams]="{ onAnamnez: id }" class="mt-4 min-h-12 w-full justify-center text-lg">
          Randevu saatini seçin
        </a>
      </section>
    }

    @if (!bayrak()) {
      <p class="sticky bottom-0 -mx-4 mt-6 border-t border-slate-200 bg-white px-4 py-1 text-base text-slate-700">
        <span aria-hidden="true">⚠ </span>Göğüs ağrısı, nefes darlığı ya da bilinç kaybı varsa beklemeyin:
        <a href="tel:112" class="inline-flex min-h-12 items-center font-semibold text-ana underline">112'yi arayın</a>
      </p>
    }
  `,
})
export class Sikayet {
  private readonly islemler = inject(OnAnamnezIslemleri);
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

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
        this.asama.set('bitti');
        this.odakla('#bitti-baslik');
      }
    } catch (e) {
      this.hata.set(hataMesaji(e));
    }
  }

  /** Form kalkıyor: klavye odağı body'ye düşmesin, banda (ve oradan 112 bağlantısına) gitsin. */
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
  private odakla(secici: string): void {
    afterNextRender(() => this.kok.nativeElement.querySelector<HTMLElement>(secici)?.focus(), { injector: this.injector });
  }
}
