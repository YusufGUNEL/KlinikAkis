import { afterNextRender, Component, computed, ElementRef, inject, Injector, input, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { HekimKararTuru, OnAnamnezDetayi, SohbetKaydi } from '../cekirdek/api';
import { OnAnamnezIslemleri } from '../cekirdek/onanamnez';
import { hataMesaji } from '../cekirdek/randevu';
import { AiKarti, HekimOnayi } from '../ortak/ai-karti';

const SIDDET: Record<string, string> = { Hafif: 'Hafif', Orta: 'Orta', Siddetli: 'Şiddetli' };
const ACILIYET: Record<string, string> = { Rutin: 'Rutin', Oncelikli: 'Öncelikli' };
const DURUM_ETIKETI: Record<string, string> = {
  Taslak: 'taslak',
  Onaylandi: 'onaylandı',
  Duzenlendi: 'düzenlendi',
  Reddedildi: 'reddedildi',
};
const KARAR_EYLEMI: Partial<Record<string, string>> = {
  Onaylandi: 'onayladı',
  Duzenlendi: 'düzenledi',
  Reddedildi: 'reddetti',
};

/**
 * Doktorun ön anamnez kartı (taslak 4, SPEC §10.2). Her madde kaynağı olan sohbet adımına bağlı; kaynak yoksa
 * "kaynak yok". Onayla / Düzenle / Reddet; karardan sonra kenar düz olur. Bölüm kararı kodda doğrulanmıştır,
 * burada yalnızca gösterilir. Hasta notu (taslak 2 "Düzelt") hekim metninden ayrı görünür.
 */
@Component({
  selector: 'app-onanamnez-karti',
  imports: [AiKarti, ButtonModule],
  template: `
    @if (detay().kart; as k) {
      <p id="onanamnez-karti-baslik" tabindex="-1" class="sr-only">Ön anamnez kartı, durum: {{ durumEtiketi[detay().durum] }}</p>
      <app-ai-karti [kaynak]="k.kaynak" [zaman]="k.zaman" [model]="k.model ?? ''" [kotaDoldu]="k.kotaDoldu" [onay]="onay()">
        @if (detay().karar?.duzeltme; as d) {
          <p class="text-xs font-medium text-slate-600">Hekimin düzelttiği özet</p>
          <p class="mt-1 whitespace-pre-line text-slate-900" data-test="hekim-duzeltmesi">{{ d }}</p>
          <p class="mt-2 text-xs text-slate-600">Makine taslağı: <span class="line-through">{{ k.taslak.ozet }}</span></p>
        } @else {
          <p [class.line-through]="detay().durum === 'Reddedildi'" data-test="ozet">{{ k.taslak.ozet }}</p>
        }

        <ul class="mt-2 flex flex-col gap-1" aria-label="Belirtiler ve kaynakları">
          @for (m of k.taslak.kaynaklar; track $index) {
            <li class="flex flex-wrap items-baseline gap-x-2">
              <span>• {{ m.madde }}</span>
              @if (adim(m.sohbetAdimi); as a) {
                <span class="text-xs text-ai" [title]="a.soru + ' → ' + a.cevap">
                  [{{ a.adim }}. adım<span class="sr-only">: {{ a.cevap }}</span>]
                </span>
              } @else {
                <span class="text-xs text-slate-600" data-test="kaynak-yok">[kaynak yok]</span>
              }
            </li>
          }
        </ul>

        <dl class="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
          <dt class="text-slate-600">Süre</dt>
          <dd>{{ k.taslak.sure }} <span class="text-xs text-ai">[2. adım]</span></dd>
          <!-- Şiddet ve aciliyet modelden değil, sohbet cevaplarından kodda hesaplanır. -->
          <dt class="text-slate-600">Şiddet</dt>
          <dd>{{ siddet[k.taslak.siddet] }} <span class="text-xs text-ai">[3. adım]</span></dd>
          <dt class="text-slate-600">Aciliyet</dt>
          <dd>{{ aciliyet[k.taslak.aciliyet] }} <span class="text-xs text-ai">[3., 4. adım]</span></dd>
          <dt class="text-slate-600">Bölüm</dt>
          <dd>
            {{ k.onerilen.ad }}
            @if (k.alternatif; as alt) { <span class="text-slate-600">· alternatif {{ alt.ad }}</span> }
            <span class="text-xs text-slate-600">[listeden, kodda doğrulandı]</span>
          </dd>
          <dt class="text-slate-600">Gerekçe</dt>
          <dd>{{ k.taslak.gerekce }} <span class="text-xs text-slate-600" data-test="kaynak-yok">[kaynak yok]</span></dd>
        </dl>

        @if (k.taslak.doktoraSorular.length) {
          <p class="mt-3 font-medium">
            Sorulabilecekler <span class="text-xs font-normal text-slate-600" data-test="kaynak-yok">[model önerisi · kaynak yok]</span>
          </p>
          <ul class="list-disc pl-5">
            @for (q of k.taslak.doktoraSorular; track $index) {
              <li>{{ q }}</li>
            }
          </ul>
        }

        @if (duzenleniyor()) {
          <form aiButonlar class="flex w-full flex-col gap-2" (submit)="$event.preventDefault(); karar('duzenle')">
            <label for="hekim-metni" class="font-medium text-slate-900">Özeti düzeltin</label>
            <textarea
              id="hekim-metni"
              rows="4"
              maxlength="2000"
              class="rounded-md border border-slate-500 p-2"
              [value]="metin()"
              (input)="metin.set($any($event.target).value)"
            ></textarea>
            <div class="flex gap-2">
              <button pButton type="submit" [disabled]="islemde() || !metin().trim()">Kaydet ve onayla</button>
              <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" (click)="vazgec()">Vazgeç</button>
            </div>
          </form>
        } @else if (detay().durum === 'Taslak') {
          <ng-container aiButonlar>
            <button pButton type="button" [disabled]="islemde()" (click)="karar('onayla')">Onayla</button>
            <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" [disabled]="islemde()" (click)="duzenleBasla()">
              Düzenle
            </button>
            @if (reddetSoruluyor()) {
              <!-- Ret geri alınamaz: ikinci basışla onaylanır. -->
              <button pButton type="button" severity="secondary" variant="outlined" class="ikincil" id="reddet-onay" [disabled]="islemde()" (click)="karar('reddet')">
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

      @if (detay().hastaNotu; as n) {
        <p class="mt-2 rounded-md border border-slate-300 bg-white p-2" data-test="hasta-notu">
          <span class="font-medium">Hasta notu:</span> “{{ n }}”
        </p>
      } @else if (detay().hastaOnayi) {
        <p class="mt-2 text-slate-700"><span class="text-basari" aria-hidden="true">✓ </span>Hasta özeti doğruladı.</p>
      }
      @if (hata()) {
        <p class="mt-2 font-medium text-uyari" role="alert"><span aria-hidden="true">⚠</span> {{ hata() }}</p>
      }
    } @else {
      <p class="text-slate-700">Bu şikayet sohbeti için taslak yok.</p>
    }
  `,
})
export class OnAnamnezKartiBileseni {
  private readonly islemler = inject(OnAnamnezIslemleri);
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly detay = input.required<OnAnamnezDetayi>();
  /** Karar verildi: üst bileşen kartı, listeyi ve yan paneli yeniden çeker. */
  readonly degisti = output<void>();

  protected readonly siddet = SIDDET;
  protected readonly aciliyet = ACILIYET;
  protected readonly durumEtiketi = DURUM_ETIKETI;
  protected readonly duzenleniyor = signal(false);
  protected readonly metin = signal('');
  protected readonly islemde = signal(false);
  protected readonly hata = signal('');
  protected readonly reddetSoruluyor = signal(false);

  protected readonly onay = computed<HekimOnayi | null>(() => {
    const k = this.detay().karar;
    return k ? { hekimAd: k.hekimAd, zaman: k.zaman, eylem: KARAR_EYLEMI[k.durum], reddedildi: k.durum === 'Reddedildi' } : null;
  });

  protected adim(no: number): SohbetKaydi | undefined {
    return no > 0 ? this.detay().sohbet.find((s) => s.adim === no) : undefined;
  }

  protected duzenleBasla(): void {
    this.metin.set(this.detay().kart?.taslak.ozet ?? '');
    this.duzenleniyor.set(true);
    this.odakla('#hekim-metni');
  }

  protected vazgec(): void {
    this.duzenleniyor.set(false);
    this.odakla('#onanamnez-karti-baslik');
  }

  protected reddetSor(): void {
    this.reddetSoruluyor.set(true);
    this.odakla('#reddet-onay');
  }

  protected async karar(tur: HekimKararTuru): Promise<void> {
    this.islemde.set(true);
    this.hata.set('');
    try {
      await this.islemler.karar(this.detay().id, tur, tur === 'duzenle' ? this.metin().trim() : null);
      this.duzenleniyor.set(false);
      this.reddetSoruluyor.set(false);
      this.degisti.emit();
      // Butonlar kalkıyor: odak body'ye düşmesin, kararın yazıldığı karta gitsin.
      this.odakla('#onanamnez-karti-baslik');
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
