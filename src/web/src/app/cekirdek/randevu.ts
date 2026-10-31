import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HastaRandevusu, RandevuEylemi, RandevuSatiri } from './api';

const YOLLAR: Record<RandevuEylemi, string> = {
  Kabul: 'kabul',
  Cagir: 'cagir',
  Basla: 'basla',
  Kapat: 'kapat',
  Iptal: 'iptal',
  Gelmedi: 'gelmedi',
};

/** Randevu yazma uçları. Durum kuralı sunucudaki durum makinesindedir; burada yalnızca istek atılır. */
@Service()
export class RandevuIslemleri {
  private readonly http = inject(HttpClient);

  al(slotId: string, onAnamnezId: string | null = null): Promise<HastaRandevusu> {
    return firstValueFrom(this.http.post<HastaRandevusu>('/api/randevular', { slotId, onAnamnezId }));
  }

  uygula(randevuId: string, eylem: RandevuEylemi): Promise<RandevuSatiri> {
    return firstValueFrom(this.http.post<RandevuSatiri>(`/api/randevular/${randevuId}/${YOLLAR[eylem]}`, null));
  }
}

/** ProblemDetails.detail (sunucudaki Türkçe mesaj) ya da durum koduna göre genel mesaj. */
export function hataMesaji(hata: unknown): string {
  if (hata instanceof HttpErrorResponse) {
    const govde = hata.error as { detail?: unknown; errors?: Record<string, unknown> } | null;
    if (typeof govde?.detail === 'string' && govde.detail) return govde.detail;
    // ValidationProblem: Türkçe mesaj alan sözlüğünde (title İngilizce, detail boş).
    const ilk = Object.values(govde?.errors ?? {}).flat()[0];
    if (typeof ilk === 'string' && ilk) return ilk;
    if (hata.status === 401 || hata.status === 403) return 'Bu işlem için yetkiniz yok ya da oturumunuzun süresi doldu.';
  }
  return 'İşlem yapılamadı. Bağlantıyı kontrol edip yeniden deneyin.';
}

// İstanbul gününü "30.10.2026" olarak verir; karşılaştırma anahtarı da gösterilen tarih de budur.
const GUN = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', day: '2-digit', month: '2-digit', year: 'numeric' });
const SAAT = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', hour: '2-digit', minute: '2-digit' });
// Türkiye'de yaz saati yok (sabit UTC+3, app.config.ts'deki '+0300' ile aynı varsayım): bir gün = 24 saat.
const GUN_MS = 86_400_000;

/** "Bugün 14:20" / "Yarın 09:10" / "01.11.2026 09:10" — gün İstanbul'a göre (SPEC §10.5). */
export function gunEtiketi(isoUtc: string, simdi: Date = new Date()): string {
  const tarih = new Date(isoUtc);
  const gun = GUN.format(tarih);
  const saat = SAAT.format(tarih);
  if (gun === GUN.format(simdi)) return `Bugün ${saat}`;
  if (gun === GUN.format(new Date(simdi.getTime() + GUN_MS))) return `Yarın ${saat}`;
  return `${gun} ${saat}`;
}

/** Liste satırı: bugünse yalnızca "14:20", değilse "Yarın 00:30" (gece demosunda yarının saatleri karışmasın). */
export function saatEtiketi(isoUtc: string, simdi: Date = new Date()): string {
  const etiket = gunEtiketi(isoUtc, simdi);
  return etiket.startsWith('Bugün ') ? etiket.slice('Bugün '.length) : etiket;
}

/** Eylem bittikten sonra ekran okuyucuya okunan cümle ("A-001 Ayşe K.: hasta çağrıldı"). */
export const EYLEM_SONUCU: Record<RandevuEylemi, string> = {
  Kabul: 'kabul edildi',
  Cagir: 'hasta çağrıldı',
  Basla: 'muayene başladı',
  Kapat: 'vizit kapatıldı',
  Iptal: 'randevu iptal edildi',
  Gelmedi: 'gelmedi olarak işaretlendi',
};

export const EYLEM_ETIKETI: Record<RandevuEylemi, string> = {
  Kabul: 'Kabul et',
  Cagir: 'Hastayı çağır',
  Basla: 'Muayeneye başla',
  Kapat: 'Vizit kapat',
  Iptal: 'İptal',
  Gelmedi: 'Gelmedi',
};
