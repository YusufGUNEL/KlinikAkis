# Mimari

Tek Docker imajı: Angular uygulaması, Minimal API ve SignalR hub'ı aynı origin'den sunulur (CORS yok,
WebSocket vekil sorunu yok). Ayrıntılı gereksinimler: [SPEC.md](SPEC.md).

## Bileşenler

```mermaid
flowchart LR
    subgraph Istemci["Tarayıcı (Angular SPA)"]
        H["Hasta<br/>telefon"]
        D["Doktor<br/>dizüstü"]
        K["Kabul"]
        T["TV sıra ekranı"]
    end

    subgraph Api["KlinikAkis.Api (ASP.NET Core, .NET 10)"]
        REST["Minimal API<br/>/api/*"]
        HUB["SignalR hub<br/>/hubs/klinik"]
        OI["OutboxIsleyici<br/>(BackgroundService)"]
    end

    subgraph Domain["KlinikAkis.Domain (paket bağımlılığı yok)"]
        DM["RandevuDurumMakinesi"]
        TD["TahlilDegerlendirici<br/>H/L, kritik eşik"]
        KB["KirmiziBayrakMotoru"]
        ICD["Icd10Listesi<br/>kapalı liste"]
        TM["TurkceMetin"]
    end

    subgraph Infra["KlinikAkis.Infrastructure"]
        DB["KlinikDbContext<br/>EF Core"]
        AH["AiHatti"]
        MS["Maskeleyici"]
        OG["OutboxGonderici"]
    end

    LLM[("IChatClient<br/>Gemini · Fake<br/>(Ollama: Aşama 10)")]
    SQL[("SQLite (yerel)<br/>PostgreSQL (Neon)")]
    EN["e-Nabız<br/>(simülasyon)"]

    Istemci -- "REST (JWT)" --> REST
    HUB -. "{tur, id} sinyali" .-> Istemci
    REST --> Domain
    REST --> AH
    AH --> MS
    AH --> KB
    AH --> LLM
    REST --> DB --> SQL
    REST -- "IOlayYayinci" --> HUB
    OI --> OG --> EN
```

Bağımlılık yönü: `Api → Infrastructure → Domain`, `Api → Domain`. Sınıflandırma kararları (randevu durumu,
tahlil işareti, kırmızı bayrak, ICD-10 listesi) Domain'de, saf C# olarak test edilir.

## AI hattı

Her AI özelliği (ön anamnez, tahlil açıklaması, epikriz, ICD-10 önerisi) aynı `AiHatti` yolundan geçer.
Her dal `AiAuditLog`'a yazar; doktor ekranındaki "Modele giden veri" paneli bu kaydı gösterir.

```mermaid
flowchart TD
    IP{"IP hız sınırı<br/>dakikada 10 AI isteği"} -- "aşıldı" --> P["429 ProblemDetails"]
    IP -- "uygun" --> G["Girdi (sohbet / tahlil / muayene)"]
    G --> M["Maskeleme (modele gidecek metin)<br/>TC, telefon, e-posta, tarih, ad → [TC] [TELEFON] [TARIH] [AD]<br/>hasta → takma ad, yaş → aralık"]
    M --> KK{"Kırmızı bayrak kuralı (ön anamnez)<br/>maskesiz ham metinde, sunucu içinde"}
    KK -- "kırmızı bayrak" --> KS["Kural sonucu<br/>112 bandı · LLM çağrılmaz"]
    KK -- "temiz" --> Q{"Günlük kota"}
    Q -- "doldu" --> Y["Kural tabanlı yedek<br/>+ 'demo kotası' rozeti"]
    Q -- "uygun" --> C["IChatClient<br/>JSON şema + enum · 15 sn zaman aşımı"]
    C --> V{"Sunucu doğrulaması<br/>listede mi, aralıkta mı, işaretle çelişiyor mu"}
    V -- "geçersiz / hata / zaman aşımı" --> Y
    V -- "geçerli" --> TS["Taslak (mor, kesik kenar)"]
    Y --> TS
    KS --> A[("AiAuditLog")]
    TS --> A
    TS --> HK{"Hekim kararı"}
    HK -- "Onayla / Düzenle" --> KL["Klinik kayıt ·<br/>hastaya görünen alan"]
    HK -- "Reddet" --> R["Kayda geçmez"]
```

Model hiçbir zaman karar vermez: işaret, bölüm listesi ve kırmızı bayrak koddan gelir; model yalnızca şemaya
bağlı JSON içinde cümle kurar. Önerilen bölüm ya da ICD-10 kodu listede yoksa, tahlil cümlesi kodun verdiği işaretle
çelişiyorsa yanıt sunucuda reddedilir ve yedeğe düşülür; geçersiz alternatif bölüm ve aralık dışı kaynak adımı ise
yanıtı düşürmeden temizlenir ("kaynak yok").

## Gerçek zamanlı akış

REST doğruluğun kaynağıdır; SignalR yalnızca "değişti, yeniden çek" der. Gruplar sunucuda JWT claim'lerinden
atanır, istemci grup seçemez.

```mermaid
sequenceDiagram
    participant H as Hasta (telefon)
    participant API as Minimal API
    participant HUB as KlinikHub
    participant D as Doktor (dizüstü)

    H->>API: POST /api/randevular {slotId, onAnamnezId}
    API->>API: Durum makinesi · sıra no · kayıt
    API->>HUB: IOlayYayinci: RandevuAlindi
    HUB-->>D: doctor:{hekimId} grubuna {tur, id}
    D->>API: GET /api/randevular (yeniden çek)
    API-->>D: Güncel liste + ön anamnez kartı
```

## Yayın

Çok aşamalı `Dockerfile`: Node ile Angular derlemesi → .NET SDK ile publish → Ubuntu `aspnet:10.0-noble`
çalışma imajı (ICU + tzdata; Türkçe kültür ve `Europe/Istanbul` için Alpine değil). Render ücretsiz web servisi +
Neon PostgreSQL. Şema `EnsureCreated` ile kurulur; demo verisi `POST /api/demo/reset` ile deterministik olarak
yeniden üretilir.
