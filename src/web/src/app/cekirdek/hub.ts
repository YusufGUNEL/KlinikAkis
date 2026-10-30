import { effect, inject, InjectionToken, Service, signal, untracked } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { BaglantiDurumu } from '../ortak/baglanti-gostergesi';
import { Olay } from './api';
import { Oturum } from './oturum';

/** Bağlantıyı kuran fabrika; testte sahtesi verilir. */
export const HUB_BAGLANTISI = new InjectionToken<(token: () => string) => HubConnection>('HUB_BAGLANTISI', {
  providedIn: 'root',
  factory: () => (token) =>
    new HubConnectionBuilder()
      .withUrl('/hubs/klinik', { accessTokenFactory: token })
      // Varsayılan politika 4 denemeden sonra vazgeçer; demo ekranları (kabul, TV) saatlerce açık kalır:
      // vazgeçmeden, en fazla 30 sn arayla dener.
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (b) => Math.min(30_000, 1000 * 2 ** b.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build(),
});

/**
 * Tek SignalR bağlantısı (SPEC §8). Gruplar sunucuda token'dan atanır; istemci yalnızca dinler.
 * Doğruluğun kaynağı REST: her olayda ve yeniden bağlanınca `degisiklik` artar, ekranlar buna bağlı
 * httpResource'larla listeyi yeniden çeker. DemoSifirlandi → sayfa yeniden yüklenir.
 */
@Service()
export class HubService {
  private readonly oturum = inject(Oturum);
  private readonly uretici = inject(HUB_BAGLANTISI);

  private baglanti: HubConnection | null = null;
  private baglananToken: string | null = null;
  private readonly olayAkisi = new Subject<Olay>();
  private readonly durumSinyali = signal<BaglantiDurumu>('baglaniyor');
  private readonly sayac = signal(0);
  /** İlk start başarısız olduysa, başarınca kaçan olaylar için bir kez yeniden çekilir. */
  private baslamadi = false;

  readonly durum = this.durumSinyali.asReadonly();
  readonly olaylar = this.olayAkisi.asObservable();
  /** Her olayda ve yeniden bağlanınca artar; ekranlar `hubIleYenile` ile buna bağlanır. */
  readonly degisiklik = this.sayac.asReadonly();

  constructor() {
    // Çıkışta bağlantı kapanır: eski gruplarda kalıp boş token'la sonsuza dek yeniden denemesin.
    effect(() => {
      if (!this.oturum.token()) this.kapat();
    });
  }

  /** Ekran kurulurken çağrılır; aynı oturumla ikinci çağrı mevcut bağlantıyı kullanır. */
  baglan(): void {
    const token = this.oturum.token();
    if (!token || (this.baglanti && this.baglananToken === token)) return;
    this.kapat();

    let baglanti: HubConnection;
    try {
      baglanti = this.uretici(() => this.oturum.token() ?? '');
    } catch {
      this.durumSinyali.set('koptu');
      return;
    }
    this.baglanti = baglanti;
    this.baglananToken = token;

    baglanti.on('olay', (olay: Olay) => {
      if (olay.tur === 'DemoSifirlandi') {
        location.reload();
        return;
      }
      this.olayAkisi.next(olay);
      this.sayac.update((n) => n + 1);
    });
    baglanti.onreconnecting(() => this.durumSinyali.set('baglaniyor'));
    baglanti.onreconnected(() => {
      this.durumSinyali.set('canli');
      // Kopukken kaçan olaylar için tam yeniden çekme.
      this.sayac.update((n) => n + 1);
    });
    baglanti.onclose(() => {
      if (this.baglanti !== baglanti) return;
      this.durumSinyali.set('koptu');
      this.yenidenBaslat(baglanti);
    });

    this.durumSinyali.set('baglaniyor');
    this.baslat(baglanti);
  }

  // İlk bağlantı (API henüz uyanmamış olabilir: Render ~1 dk) ya da kopma sonrası: 10 sn arayla yeniden dener.
  private baslat(baglanti: HubConnection): void {
    Promise.resolve()
      .then(() => baglanti.start())
      .then(
        () => {
          if (this.baglanti !== baglanti) return;
          this.durumSinyali.set('canli');
          // İlk denemede bağlandıysa ekranın kendi ilk çekimi yeterli; çift istek atılmaz.
          if (this.baslamadi) this.sayac.update((n) => n + 1);
          this.baslamadi = false;
        },
        () => {
          if (this.baglanti !== baglanti) return;
          this.baslamadi = true;
          this.durumSinyali.set('koptu');
          this.yenidenBaslat(baglanti);
        },
      );
  }

  private kapat(): void {
    const eski = this.baglanti;
    this.baglanti = null;
    this.baglananToken = null;
    void eski?.stop();
  }

  private yenidenBaslat(baglanti: HubConnection): void {
    setTimeout(() => this.baglanti === baglanti && this.baslat(baglanti), 10_000);
  }
}

/**
 * Hub olayında ve yeniden bağlanınca kaynakları `reload()` ile yeniden çeker (bileşen kurulurken çağrılır).
 * İsteği `degisiklik()`e bağlamak yerine reload: değer korunur, liste "Yükleniyor…"a düşüp yeniden
 * çizilmez; satırlar ve klavye odağı yerinde kalır.
 */
export function hubIleYenile(...kaynaklar: { reload(): boolean }[]): void {
  const hub = inject(HubService);
  const ilk = hub.degisiklik();
  effect(() => {
    if (hub.degisiklik() === ilk) return;
    untracked(() => kaynaklar.forEach((k) => k.reload()));
  });
}
