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

export type RandevuEylemi = 'Kabul' | 'Cagir' | 'Basla' | 'Kapat' | 'Iptal' | 'Gelmedi';

export interface SlotOgesi {
  id: string;
  baslangic: string;
  sureDk: number;
  hekimId: string;
  hekimAd: string;
  oda: string;
}

/** GET /hasta/me/randevular satırı. */
export interface HastaRandevusu {
  id: string;
  baslangic: string;
  siraNo: string;
  durum: RandevuDurumu;
  bransAd: string;
  hekimAd: string;
  oda: string;
}

/** GET /randevular satırı. hastaAd doktora "Ayşe K.", kabule maskeli "A*** K***" gelir (sunucuda). */
export interface RandevuSatiri {
  id: string;
  baslangic: string;
  siraNo: string;
  durum: RandevuDurumu;
  hastaId: string;
  hastaAd: string;
  bransAd: string;
  hekimAd: string;
  oda: string;
  acikRizaAlindi: boolean;
  takipNo: string | null;
  izinliEylemler: RandevuEylemi[];
  /** Şikayet sohbetiyle alındıysa ön anamnez kartı (yoksa null). */
  onAnamnezId: string | null;
  /** Muayeneye başlandıysa muayene (hekim notu, epikriz); yoksa null. */
  muayeneId: string | null;
}

/** TV sıra ekranındaki çağrı (taslak 8); ad sunucuda baş harfe indirgenir. */
export interface TvCagrisi {
  siraNo: string;
  oda: string;
  basHarfler: string;
  zaman: string;
}

/** SignalR olayı: yalnızca {tur, id}; istemci ilgili REST'i yeniden çeker (SPEC §8). */
export interface Olay {
  tur: string;
  id: string | null;
}

/** Soru ağacı adımı (sunucuda sabit; LLM soru üretmez — SPEC §9.4). */
export interface SohbetSorusu {
  adim: number;
  metin: string;
  secenekler: string[];
  cokluSecim: boolean;
  atlanabilir: boolean;
}

export interface KirmiziBayrak {
  kural: string;
  mesaj: string;
}

export interface OnAnamnezBaslatYaniti {
  onAnamnezId: string;
  ilkSoru: SohbetSorusu;
  toplamAdim: number;
}

export interface CevapYaniti {
  sonrakiSoru: SohbetSorusu | null;
  adim: number;
  toplamAdim: number;
  kirmiziBayrak: KirmiziBayrak | null;
}

export type OnAnamnezDurumu = 'Sohbette' | 'KirmiziBayrak' | 'Taslak' | 'Onaylandi' | 'Duzenlendi' | 'Reddedildi';

export interface OzetYaniti {
  onAnamnezId: string;
  durum: OnAnamnezDurumu;
  kaynak: AiKaynak | null;
  kirmiziBayrak: KirmiziBayrak | null;
  kart: HastaOzetKarti | null;
}

export interface HastaCevabi {
  adim: number;
  soru: string;
  cevap: string;
}

/** Hastanın gördüğü özet (taslak 2): kendi cevapları + kodda doğrulanmış bölüm; modelin serbest metni yok. */
export interface HastaOzetKarti {
  id: string;
  durum: OnAnamnezDurumu;
  zaman: string;
  cevaplar: HastaCevabi[];
  onerilen: BolumOnerisi;
  alternatif: BolumOnerisi | null;
}

export type Siddet = 'Hafif' | 'Orta' | 'Siddetli';
export type Aciliyet = 'Rutin' | 'Oncelikli';

/** Kaynak maddesi; sohbetAdimi 0 = "kaynak yok" (sunucu doğrulaması aralık dışını işaretler). */
export interface KaynakMaddesi {
  madde: string;
  sohbetAdimi: number;
}

/** §9.5 çıktı şeması (sunucuda doğrulanmış). Bölüm kararı listeden; kodla doğrulanır. */
export interface OnAnamnezTaslagi {
  ozet: string;
  semptomlar: string[];
  sure: string;
  siddet: Siddet;
  onerilenBrans: string;
  alternatifBrans: string | null;
  gerekce: string;
  aciliyet: Aciliyet;
  doktoraSorular: string[];
  kaynaklar: KaynakMaddesi[];
}

export interface BolumOnerisi {
  bransId: string;
  kod: string;
  ad: string;
}

export interface OnAnamnezKarti {
  id: string;
  durum: OnAnamnezDurumu;
  kaynak: AiKaynak;
  model: string | null;
  /** Taslağın üretildiği an (UTC ISO). */
  zaman: string;
  /** Kota doldu: model çağrılmadı, taslak kural tabanlı; kartta rozet (SPEC §9.9). */
  kotaDoldu: boolean;
  taslak: OnAnamnezTaslagi;
  onerilen: BolumOnerisi;
  alternatif: BolumOnerisi | null;
}

export interface SohbetKaydi {
  adim: number;
  soru: string;
  secimler: string[];
  metin: string | null;
  cevap: string;
}

export interface HekimKarari {
  durum: OnAnamnezDurumu;
  hekimAd: string;
  zaman: string;
  duzeltme: string | null;
}

export interface OnAnamnezDetayi {
  id: string;
  hastaId: string;
  durum: OnAnamnezDurumu;
  kirmiziBayrakKurali: string | null;
  kart: OnAnamnezKarti | null;
  sohbet: SohbetKaydi[];
  hastaOnayi: boolean;
  hastaNotu: string | null;
  karar: HekimKarari | null;
}

export type HekimKararTuru = 'onayla' | 'duzenle' | 'reddet';

/** "Modele giden veri" satırı (AiAuditLog). */
export interface DenetimSatiri {
  id: string;
  zaman: string;
  ozellik: string;
  hastaId: string | null;
  saglayici: string;
  model: string;
  istemSurumu: string;
  maskeliGirdi: string;
  girdiHash: string;
  ciktiJson: string | null;
  gecikmeMs: number;
  basarili: boolean;
  yedegeDustu: boolean;
  neden: string | null;
}

export interface ErisimSatiri {
  id: string;
  zaman: string;
  kullaniciRol: string;
  kullaniciAd: string;
  eylem: string;
}

export type EpikrizDurumu = 'Taslak' | 'Onaylandi' | 'Reddedildi';

/** Epikriz taslağı: model cümleleri (öykü, değerlendirme, plan) + kodda kurulan alerji ve tahlil satırları. */
export interface EpikrizTaslagi {
  oyku: string;
  degerlendirme: string;
  plan: string;
  alerji: string;
  tahliller: string[];
}

export interface EpikrizKarari {
  durum: EpikrizDurumu;
  hekimAd: string;
  zaman: string;
}

export interface EpikrizKarti {
  id: string;
  durum: EpikrizDurumu;
  kaynak: AiKaynak;
  model: string | null;
  zaman: string;
  kotaDoldu: boolean;
  taslak: EpikrizTaslagi;
  /** Hekim düzenleyip onayladıysa son metin. */
  sonMetin: string | null;
  karar: EpikrizKarari | null;
}

export interface MuayeneDetayi {
  id: string;
  randevuId: string;
  durum: 'Acik' | 'Kapali';
  hekimNotu: string | null;
  epikriz: EpikrizKarti | null;
}
