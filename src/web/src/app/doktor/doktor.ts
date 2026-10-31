import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { afterNextRender, Component, computed, ElementRef, inject, Injector, signal } from '@angular/core';
import { QRCodeComponent } from 'angularx-qrcode';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { DenetimSatiri, ErisimSatiri, HastaOzeti, MuayeneDetayi, OnAnamnezDetayi, RandevuEylemi, RandevuSatiri } from '../cekirdek/api';
import { HubService, hubIleYenile } from '../cekirdek/hub';
import { EYLEM_ETIKETI, EYLEM_SONUCU, hataMesaji, RandevuIslemleri, saatEtiketi } from '../cekirdek/randevu';
import { ISARET_ETIKETI, TahlilSatiri } from '../cekirdek/tahlil';
import { BaglantiGostergesi } from '../ortak/baglanti-gostergesi';
import { DurumKutusu } from '../ortak/durum-kutusu';
import { DurumRozeti } from '../ortak/durum-rozeti';
import { HastaBandi } from '../ortak/hasta-bandi';
import { simdiSinyali } from '../ortak/simdi';
import { UstCubuk } from '../ortak/ust-cubuk';
import { EpikrizSekmesi } from './epikriz-sekmesi';
import { ModeleGidenVeri, saglayiciEtiketi } from './modele-giden-veri';
import { OnAnamnezKartiBileseni } from './onanamnez-karti';
import { TahlillerSekmesi } from './tahliller-sekmesi';

/** QR'ın açtığı senaryo hastası (Ayşe K., SPEC §6). */
const QR_HASTA_ID = 'a1000000-0000-0000-0000-000000000001';
const DOKTOR_EYLEMLERI: RandevuEylemi[] = ['Cagir', 'Basla', 'Kapat'];
type Sekme = 'ozet' | 'tahliller' | 'epikriz';
/** Sekmeye göre "Modele giden veri" panelinin gösterdiği AI özelliği. */
const SEKME_OZELLIGI: Record<Sekme, string> = { ozet: 'OnAnamnez', tahliller: 'TahlilAciklama', epikriz: 'Epikriz' };
const OZELLIK_ADI: Record<string, string> = { OnAnamnez: 'ön anamnez', TahlilAciklama: 'tahlil açıklaması', Epikriz: 'epikriz' };

/**
 * Doktor çalışma alanı, 3 sütun (taslak 4, Epic Storyboard düzeni): sol günün listesi (canlı), orta hasta
 * bandı + eylem çubuğu + ön anamnez kartı, sağ sessiz uyarılar / modele giden veri / erişim kaydı.
 * Butonların etkinliği sunucudaki durum makinesinden gelen `izinliEylemler`dir; kural istemcide tekrarlanmaz.
 */
@Component({
  selector: 'app-doktor',
  imports: [
    BaglantiGostergesi,
    ButtonModule,
    DatePipe,
    DurumKutusu,
    DurumRozeti,
    EpikrizSekmesi,
    HastaBandi,
    ModeleGidenVeri,
    OnAnamnezKartiBileseni,
    QRCodeComponent,
    TabsModule,
    TahlillerSekmesi,
    UstCubuk,
  ],
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

        <main class="relative min-h-0 overflow-y-auto">
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
            <p-tabs [value]="sekme()" (valueChange)="sekmeSec($event)">
              <p-tablist>
                <p-tab value="ozet">Özet</p-tab>
                <p-tab value="tahliller" data-test="sekme-tahliller">Tahliller</p-tab>
                <p-tab value="epikriz" data-test="sekme-epikriz">Epikriz</p-tab>
              </p-tablist>
              <p-tabpanels>
                <p-tabpanel value="ozet">
                  <section aria-labelledby="onanamnez-baslik">
                    <h2 id="onanamnez-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Ön anamnez</h2>
                    <div class="mt-2 max-w-3xl">
                      @if (!r.onAnamnezId) {
                        <app-durum-kutusu tur="bos" mesaj="Randevu şikayet sohbeti olmadan alındı; ön anamnez yok." />
                      } @else if (detay.hasValue()) {
                        <app-onanamnez-karti [detay]="detay.value()" (degisti)="yenile()" />
                      } @else if (detay.error()) {
                        <app-durum-kutusu tur="hata" (yenidenDene)="detay.reload()" />
                      } @else {
                        <app-durum-kutusu tur="yukleniyor" />
                      }
                    </div>
                  </section>
                </p-tabpanel>
                <p-tabpanel value="tahliller">
                  <h2 class="sr-only">Tahliller</h2>
                  @if (tahliller.hasValue()) {
                    <app-tahliller-sekmesi [satirlar]="tahliller.value()" (degisti)="tahlilYenile()" />
                  } @else if (tahliller.error()) {
                    <app-durum-kutusu tur="hata" (yenidenDene)="tahliller.reload()" />
                  } @else {
                    <app-durum-kutusu tur="yukleniyor" />
                  }
                </p-tabpanel>
                <p-tabpanel value="epikriz">
                  <h2 class="sr-only">Epikriz</h2>
                  @if (!r.muayeneId) {
                    <app-durum-kutusu tur="bos" mesaj="Muayeneye başlayınca doktor notu ve epikriz yazılabilir." />
                  } @else if (muayene.hasValue()) {
                    <app-epikriz-sekmesi [detay]="muayene.value()" (degisti)="epikrizYenile()" />
                  } @else if (muayene.error()) {
                    <app-durum-kutusu tur="hata" (yenidenDene)="muayene.reload()" />
                  } @else {
                    <app-durum-kutusu tur="yukleniyor" />
                  }
                </p-tabpanel>
              </p-tabpanels>
            </p-tabs>
            @if (veriAcik() && sonDenetim(); as d) {
              <section id="modele-giden-veri" class="border-t border-slate-200 p-4" aria-labelledby="mgv-baslik">
                <h2 id="mgv-baslik" tabindex="-1" class="outline-none text-xs font-semibold tracking-wide text-slate-600 uppercase">Modele giden veri</h2>
                <div class="mt-2 max-w-4xl"><app-modele-giden-veri [kayit]="d" /></div>
              </section>
            }
          } @else {
            <div class="p-4"><app-durum-kutusu tur="bos" mesaj="Listeden bir hasta seçin." /></div>
          }
        </main>

        <aside class="overflow-y-auto border-l border-slate-200 bg-white p-3" aria-label="Yan panel">
          <section class="border-b border-slate-100 pb-3" aria-labelledby="uyarilar-baslik">
            <h2 id="uyarilar-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Uyarılar</h2>
            <!-- Sessiz rozet (SPEC §10.3 kademe 3): referans dışı son sonuçlar; tıklanınca Tahliller sekmesi. -->
            @if (uyarilar().length) {
              <ul class="mt-1 flex flex-col gap-1" data-test="uyarilar">
                @for (u of uyarilar(); track u.tahlilTanimiId) {
                  <li>
                    <button type="button" class="rounded px-1 text-left hover:bg-slate-50" (click)="sekme.set('tahliller')">
                      <span [class]="isaretEtiketi[u.son.isaret].renk" aria-hidden="true">●</span>
                      {{ u.ad }}
                      <span class="font-semibold" [class]="isaretEtiketi[u.son.isaret].renk">
                        <span aria-hidden="true">{{ isaretEtiketi[u.son.isaret].doktor }}</span>
                        <span class="sr-only">{{ isaretEtiketi[u.son.isaret].okunus }}</span>
                      </span>
                    </button>
                  </li>
                }
              </ul>
            } @else if (secili() && tahliller.error()) {
              <!-- Tahlil alınamadıysa "uyarı yok" denmez: yanlış güvence olur. -->
              <p class="mt-1 text-uyari" data-test="uyarilar-hata">Tahliller alınamadı; uyarılar gösterilemiyor.</p>
            } @else if (secili() && tahliller.isLoading()) {
              <p class="mt-1 text-slate-700">Yükleniyor…</p>
            } @else {
              <p class="mt-1 text-slate-700">{{ secili() ? 'Uyarı yok.' : 'Hasta seçilmedi.' }}</p>
            }
          </section>
          <section class="border-b border-slate-100 py-3" aria-labelledby="mgv-yan-baslik">
            <h2 id="mgv-yan-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Modele giden veri</h2>
            @if (sonDenetim(); as d) {
              <p class="mt-1 text-slate-700">
                <span class="tabular-nums">{{ d.zaman | date: 'HH:mm' }}</span> · {{ saglayiciEtiketi(d.saglayici) }} ·
                {{ d.saglayici === 'Kural' ? 'model çağrılmadı' : d.yedegeDustu ? 'yedek' : 'model yanıtı' }} ·
                {{ ozellikAdi[d.ozellik] ?? d.ozellik }}
              </p>
              <button
                pButton
                type="button"
                size="small"
                variant="outlined"
                class="mt-2"
                aria-controls="modele-giden-veri"
                [attr.aria-expanded]="veriAcik()"
                (click)="veriDegistir()"
              >
                {{ veriAcik() ? 'Gizle' : 'Göster' }}
              </button>
            } @else if (denetim.error()) {
              <p class="mt-1 text-uyari" role="alert"><span aria-hidden="true">⚠</span> Yüklenemedi.</p>
              <button pButton type="button" size="small" variant="outlined" class="mt-2" (click)="denetim.reload()">Yeniden dene</button>
            } @else if (denetim.isLoading()) {
              <p class="mt-1 text-slate-600">Yükleniyor…</p>
            } @else {
              <p class="mt-1 text-slate-700">{{ secili() ? 'Henüz model çağrısı yok.' : 'Hasta seçilmedi.' }}</p>
            }
          </section>
          <section class="py-3" aria-labelledby="erisim-baslik">
            <h2 id="erisim-baslik" class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Erişim kaydı</h2>
            @if (erisim.hasValue() && erisim.value().length) {
              <ol class="mt-1 flex flex-col gap-1" data-test="erisim-kaydi">
                @for (e of erisim.value().slice(0, 8); track e.id) {
                  <li class="text-slate-700">
                    <span class="tabular-nums">{{ e.zaman | date: 'HH:mm' }}</span> · {{ e.kullaniciAd }} · {{ e.eylem }}
                  </li>
                }
              </ol>
            } @else if (erisim.error() || ozet.error()) {
              <p class="mt-1 text-uyari" role="alert"><span aria-hidden="true">⚠</span> Yüklenemedi.</p>
              <button pButton type="button" size="small" variant="outlined" class="mt-2" (click)="erisimYenile()">Yeniden dene</button>
            } @else if (erisim.isLoading() || ozet.isLoading()) {
              <p class="mt-1 text-slate-600">Yükleniyor…</p>
            } @else {
              <p class="mt-1 text-slate-700">{{ secili() ? 'Kayıt yok.' : 'Hasta seçilmedi.' }}</p>
            }
          </section>
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
  protected readonly saglayiciEtiketi = saglayiciEtiketi;
  protected readonly ozellikAdi = OZELLIK_ADI;
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
  private readonly seciliOnAnamnezId = computed(() => this.secili()?.onAnamnezId);
  protected readonly detay = httpResource<OnAnamnezDetayi>(() => {
    const id = this.seciliOnAnamnezId();
    return id ? `/api/onanamnez/${id}` : undefined;
  });
  // Başlıklar CSS uppercase ile büyür: lang="tr" olduğu için "Erişim" → "ERİŞİM" doğru çıkar (SPEC §10.5).
  protected readonly denetim = httpResource<DenetimSatiri[]>(() => {
    const id = this.seciliHastaId();
    return id ? { url: '/api/ai/denetim', params: { hastaId: id } } : undefined;
  });
  // Panel seçili kartın çağrısını göstersin: hastanın sonradan başlattığı başka bir sohbetin kaydını değil.
  // Taslak zamanı hat bittikten sonra yazılır; kartın kaydı o andan önceki en yeni kayıttır (liste yeniden eskiye).
  // Tahliller ve Epikriz sekmelerinde o özelliğin son çağrısı gösterilir.
  protected readonly sonDenetim = computed(() => {
    if (!this.denetim.hasValue()) return null;
    const ozellik = SEKME_OZELLIGI[this.sekme()];
    const kayitlar = this.denetim.value().filter((k) => k.ozellik === ozellik);
    if (ozellik !== 'OnAnamnez') return kayitlar[0] ?? null;
    const kartZamani = this.detay.hasValue() ? this.detay.value().kart?.zaman : undefined;
    if (!kartZamani) return kayitlar[0] ?? null;
    const sinir = Date.parse(kartZamani);
    return kayitlar.find((k) => Date.parse(k.zaman) <= sinir) ?? kayitlar[0] ?? null;
  });
  protected readonly veriAcik = signal(false);
  protected readonly sekme = signal<Sekme>('ozet');
  protected readonly isaretEtiketi = ISARET_ETIKETI;
  protected readonly tahliller = httpResource<TahlilSatiri[]>(() => {
    const id = this.seciliHastaId();
    return id ? `/api/hastalar/${id}/tahliller` : undefined;
  });
  private readonly seciliMuayeneId = computed(() => this.secili()?.muayeneId);
  protected readonly muayene = httpResource<MuayeneDetayi>(() => {
    const id = this.seciliMuayeneId();
    return id ? `/api/muayeneler/${id}` : undefined;
  });
  protected readonly uyarilar = computed(() =>
    this.tahliller.hasValue() ? this.tahliller.value().filter((t) => t.son.isaret !== 'Normal') : [],
  );
  // Hasta özeti çekilince "Hasta kaydını görüntüledi" satırı yazılır; kayıt ondan sonra okunsun ki o satır da gelsin.
  protected readonly erisim = httpResource<ErisimSatiri[]>(() => {
    const id = this.seciliHastaId();
    return id && this.ozet.hasValue() ? `/api/hastalar/${id}/erisim` : undefined;
  });
  private readonly injector = inject(Injector);

  constructor() {
    this.hub.baglan();
    hubIleYenile(this.liste, this.detay, this.denetim, this.erisim, this.tahliller, this.muayene);
  }

  /** Panel ana alanın altında açılır: görünür alana kaydır, odağı başlığına ver. */
  protected veriDegistir(): void {
    this.veriAcik.set(!this.veriAcik());
    if (this.veriAcik())
      afterNextRender(() => this.kok.nativeElement.querySelector<HTMLElement>('#mgv-baslik')?.focus(), {
        injector: this.injector,
      });
  }

  /** Erişim kaydı özete bağlı: özet düştüyse önce o yeniden çekilir, erişim kendiliğinden gelir. */
  protected erisimYenile(): void {
    if (this.ozet.error()) this.ozet.reload();
    else this.erisim.reload();
  }

  protected sekmeSec(deger: string | number | undefined): void {
    if (deger === 'ozet' || deger === 'tahliller' || deger === 'epikriz') this.sekme.set(deger);
  }

  /** Taslak/yayın sonrası: tablo, denetim paneli ve erişim kaydı (yayın satırı). */
  protected tahlilYenile(): void {
    this.tahliller.reload();
    this.denetim.reload();
    this.erisim.reload();
  }

  /** Not, taslak ya da karar sonrası: muayene kartı, denetim paneli ve erişim kaydı (karar satırı). */
  protected epikrizYenile(): void {
    this.muayene.reload();
    this.denetim.reload();
    this.erisim.reload();
  }

  /** Karar sonrası: kart, liste ve yan panel (erişim kaydı yeni satır aldı). */
  protected yenile(): void {
    this.liste.reload();
    this.detay.reload();
    this.denetim.reload();
    this.erisim.reload();
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
      this.duyuru.set(`${r.siraNo} ${r.hastaAd}: ${EYLEM_SONUCU[eylem]}`);
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
