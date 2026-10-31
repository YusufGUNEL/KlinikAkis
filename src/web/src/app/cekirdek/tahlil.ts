import { formatNumber } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AiKaynak } from './api';

/** Kodla hesaplanan işaret (SPEC §9.6, `TahlilDegerlendirici`). Arayüz yeniden hesaplamaz, yalnızca gösterir. */
export type Isaret = 'Normal' | 'Yuksek' | 'Dusuk' | 'KritikYuksek' | 'KritikDusuk';

export type BilgilendirmeSecimi = 'TelefonlaBilgilendirildi' | 'Bilgilendirilecek';

export interface IsaretEtiketi {
  /** Doktor tablosu: "▲ Y". */
  doktor: string;
  /** Ekran okuyucu için doktor etiketinin açık hâli. */
  okunus: string;
  /** Hasta ekranı: "▲ Referansın üstünde". */
  hasta: string;
  /** Renk sınıfı: yeşil / amber / kırmızı (kırmızı yalnızca kritik değer, SPEC §10.1). */
  renk: string;
  kritik: boolean;
}

/** İşaret etiketlerinin tek kaynağı (SPEC §10.1 tablosu). Renk asla tek ipucu değil: ikon + metin. */
export const ISARET_ETIKETI: Record<Isaret, IsaretEtiketi> = {
  Normal: { doktor: '✓', okunus: 'Normal', hasta: '✓ Normal aralıkta', renk: 'text-basari', kritik: false },
  Yuksek: { doktor: '▲ Y', okunus: 'Yüksek', hasta: '▲ Referansın üstünde', renk: 'text-uyari', kritik: false },
  Dusuk: { doktor: '▼ D', okunus: 'Düşük', hasta: '▼ Referansın altında', renk: 'text-uyari', kritik: false },
  KritikYuksek: {
    doktor: '‼ KY',
    okunus: 'Kritik yüksek',
    hasta: '‼ Çok yüksek · doktorunuz sizinle iletişime geçecek',
    renk: 'text-tehlike',
    kritik: true,
  },
  KritikDusuk: {
    doktor: '‼ KD',
    okunus: 'Kritik düşük',
    hasta: '‼ Çok düşük · doktorunuz sizinle iletişime geçecek',
    renk: 'text-tehlike',
    kritik: true,
  },
};

/** Hastaya yayınlanan her açıklamanın altındaki sabit metin (SPEC §9.6). */
export const HASTA_UYARI_METNI = 'Bu açıklama bilgilendirme amaçlıdır, tanı yerine geçmez.';

/** DecimalPipe biçimi: testin sabit basamağı ("7,0", "128"). */
export function basamak(ondalik: number): string {
  return `1.${ondalik}-${ondalik}`;
}

/** Türkçe sayı (ondalık virgül); ekran okuyucu metinlerinde ve trend satırında. */
export function sayi(deger: number, ondalik: number): string {
  return formatNumber(deger, 'tr', basamak(ondalik));
}

export function trendMetni(degerler: number[], ondalik: number): string {
  return degerler.map((d) => sayi(d, ondalik)).join(' → ');
}

export interface TahlilDegeri {
  id: string;
  deger: number;
  zaman: string;
  isaret: Isaret;
  hastayaYayinlandi: boolean;
}

/** Son sonucun açıklama durumu: AI taslağı (kart başlığı bilgisiyle) ve yayınlanan hekim metni. */
export interface SonucAciklamasi {
  kaynak: AiKaynak | null;
  taslak: string | null;
  model: string | null;
  zaman: string | null;
  kotaDoldu: boolean;
  yayinlanan: string | null;
  yayinZamani: string | null;
  yayinlayanHekim: string | null;
}

/** Doktor tahlil sekmesi satırı (GET /hastalar/{id}/tahliller). */
export interface TahlilSatiri {
  tahlilTanimiId: string;
  ad: string;
  hastaDostuAd: string;
  loincKodu: string;
  birim: string;
  altSinir: number;
  ustSinir: number;
  kritikAlt: number | null;
  kritikUst: number | null;
  ondalik: number;
  not: string;
  son: TahlilDegeri;
  trend: TahlilDegeri[];
  aciklama: SonucAciklamasi;
}

export interface TrendNoktasi {
  deger: number;
  zaman: string;
}

/** Hastanın gördüğü sonuç (GET /hasta/me/sonuclar): yalnızca yayınlanmış değer ve hekimin yayınladığı metin. */
export interface HastaSonucu {
  sonucId: string;
  ad: string;
  hastaDostuAd: string;
  loincKodu: string;
  birim: string;
  altSinir: number;
  ustSinir: number;
  ondalik: number;
  not: string;
  deger: number;
  zaman: string;
  isaret: Isaret;
  aciklama: string | null;
  yayinZamani: string;
  yayinlayanHekim: string | null;
  trend: TrendNoktasi[];
}

/** Tahlil açıklaması ve yayın uçları. İşaret kararı sunucuda; burada yalnızca istek atılır. */
@Service()
export class TahlilIslemleri {
  private readonly http = inject(HttpClient);

  /** AI açıklama taslağı (ya da kural tabanlı yedek) üretir; hastaya gitmez. */
  aciklama(sonucId: string): Promise<SonucAciklamasi> {
    return firstValueFrom(this.http.post<SonucAciklamasi>(`/api/tahliller/${sonucId}/aciklama`, null));
  }

  /** Hekimin metniyle hastaya yayınlar; kritik değerde bilgilendirme seçimi zorunlu (SPEC §10.3). */
  yayinla(sonucId: string, aciklama: string, bilgilendirme: BilgilendirmeSecimi | null): Promise<unknown> {
    return firstValueFrom(
      this.http.post('/api/tahliller/yayinla', { sonuclar: [{ sonucId, aciklama }], bilgilendirme }),
    );
  }
}
