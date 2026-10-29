// API yanıt tipleri (camelCase JSON; enum'lar metin, zamanlar 'Z' ekli UTC ISO 8601 — SPEC §10.5).

export type Rol = 'hasta' | 'doktor' | 'kabul' | 'tv';

export const ROLLER: readonly Rol[] = ['hasta', 'doktor', 'kabul', 'tv'];

export interface KimlikOgesi {
  id: string;
  ad: string;
  aciklama: string;
}

export interface GirisYaniti {
  token: string;
  rol: Rol;
  kimlikId: string | null;
  ad: string;
  bitis: string;
}

export interface BransOgesi {
  id: string;
  kod: string;
  ad: string;
  hekimSayisi: number;
}

export interface SeedSayimlari {
  brans: number;
  hekim: number;
  hasta: number;
  slot: number;
  randevu: number;
  tahlilSonucu: number;
}

export type Cinsiyet = 'K' | 'E';

export interface HastaOzeti {
  id: string;
  ad: string;
  soyad: string;
  yas: number;
  cinsiyet: Cinsiyet;
  tcMaskeli: string;
  takmaAd: string;
  alerjiler: string[];
  kronikHastaliklar: string[];
  kullandigiIlaclar: string[];
  acikRizaAlindi: boolean;
  sentetik: boolean;
}

export type RandevuDurumu =
  | 'Planlandi'
  | 'KabulEdildi'
  | 'Cagrildi'
  | 'Muayenede'
  | 'Tamamlandi'
  | 'Iptal'
  | 'Gelmedi';

export type AiKaynak = 'Llm' | 'Yedek' | 'Kural';
