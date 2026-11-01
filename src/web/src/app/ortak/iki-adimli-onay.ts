import { signal } from '@angular/core';

/**
 * Geri alınamaz düğme (demo sıfırlama) için iki adım: ilk tıklama onay ister, süre içinde ikinci tıklama çalıştırır.
 * Tarayıcı diyaloğu kullanılmaz (sunumda ekranı kilitlemesin); süre dolunca düğme eski haline döner.
 */
export class IkiAdimliOnay {
  readonly bekliyor = signal(false);
  /** Etiket değişimi her ekran okuyucuda okunmuyor; bekleme durumu ayrıca canlı bölgede duyurulur. */
  readonly duyuru = 'Sıfırlamak için düğmeye 4 saniye içinde yeniden basın.';
  private zamanlayici: ReturnType<typeof setTimeout> | undefined;

  constructor(private readonly sureMs = 4000) {}

  /** İlk çağrıda onay bekler ve false döner; süre içinde ikinci çağrıda true döner. */
  onaylandi(): boolean {
    clearTimeout(this.zamanlayici);
    if (this.bekliyor()) {
      this.bekliyor.set(false);
      return true;
    }
    this.bekliyor.set(true);
    this.zamanlayici = setTimeout(() => this.bekliyor.set(false), this.sureMs);
    return false;
  }
}
