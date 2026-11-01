# KlinikAkış — Proje Raporu

**Tarih:** 1 Kasım 2026
**Proje:** [YusufGUNEL/KlinikAkis](https://github.com/YusufGUNEL/KlinikAkis)
**Canlı demo:** [klinikakis.onrender.com](https://klinikakis.onrender.com)

## 1. Kısa özet

KlinikAkış, poliklinik akışını randevudan epikrize kadar gösteren sentetik verili bir demo
uygulamasıdır. Hasta telefonu, doktor dizüstü ekranı, kabul masası ve TV sıra ekranı aynı
uygulamayı kullanır; REST veri kaynağı, SignalR ise yalnızca “yeniden çek” sinyali olarak
kullanılır.

Projenin temel ilkesi **“karar kodda, cümle modelde”** yaklaşımıdır:

- H/L işareti, kırmızı bayrak, önerilebilecek bölüm ve ICD-10 kapalı listesi deterministik kodla
  belirlenir.
- Yapay zekâ yalnızca açıklama ve özet taslağı üretir.
- Hiçbir AI çıktısı hekim onayı olmadan klinik kayda veya hastanın gördüğü alana yazılmaz.
- Tüm örnekler sentetiktir; uygulama tıbbi cihaz değildir.

## 2. Gösterilen uçtan uca akış

1. Doktor ekranındaki QR ile Ayşe K. adlı sentetik hasta telefonu açılır.
2. Hasta şikayet sohbetini tamamlar; kırmızı bayrak temizse AI ön anamnez ve bölüm önerisi
   taslağı oluşturulur.
3. Hasta özeti onaylar ve randevu alır.
4. Randevu doktor ve kabul ekranlarına SignalR ile canlı düşer; kabulde takip numarası üretilir.
5. Doktor, mor ve kesik çizgili ön anamnez taslağını inceler; onay, düzenleme veya ret kararı
   verebilir.
6. Doktor hastayı çağırınca TV ekranı ve hasta telefonu güncellenir.
7. Tahlil sonuçlarında H/L işareti koddan gelir; model yalnızca hastaya yönelik açıklama cümlesi
   üretir. Doktor yayınlamadan hasta bu metni göremez.
8. Epikriz ve kapalı listeden ICD-10 önerisi hekim onayından sonra vizit kapanışına taşınır.
9. e-Nabız gönderim kutusu 101/103/105 paketlerini simüle eder; 105 paketi ilk denemede iş
   kuralı hatası verip “Yeniden dene” ile tamamlanır.
10. Göğüs ağrısı + terleme gibi kırmızı bayrakta 112 yönlendirmesi yapılır; LLM çağrılmaz ve
    denetim kaydına kural adı yazılır.

## 3. Teknik yaklaşım

| Katman | Kullanılan yapı |
|---|---|
| Backend | ASP.NET Core .NET 10 Minimal API |
| Veri | EF Core 10; yerelde SQLite, yayında PostgreSQL uyumlu |
| Canlı senkron | SignalR; payload yalnızca tür ve kimlik |
| Frontend | Angular 22 standalone bileşenler, signals, reactive forms |
| UI | PrimeNG Aura + Tailwind; teal ana renk, mor AI taslak kartı |
| AI | `IChatClient` üzerinden Fake/Gemini sağlayıcıları, JSON şema ve sunucu doğrulaması |
| Test | xUnit, Angular Vitest |
| Yayın | Tek Docker imajı; API, Angular ve SignalR aynı origin |

## 4. Güvenlik ve güvenilirlik kararları

- TC, telefon, e-posta, tarih ve kayıtlı ad/soyad modele gitmeden maskelenir.
- Modelden önce kırmızı bayrak kuralları çalışır; kritik durumda model çağrısı yapılmaz.
- Kota, zaman aşımı, sağlayıcı hatası ve geçersiz JSON için kural tabanlı yedek vardır.
- Her AI çağrısı sağlayıcı, model, istem sürümü, maskeli giriş, çıktı, süre ve yedek nedenini
  denetim kaydına alır.
- SignalR grupları sunucuda claim ile atanır; istemci grup seçemez.
- API anahtarı ve bağlantı dizesi repoda tutulmaz.
- Hasta ekranında alerji alanı her zaman görünür; kırmızı renk yalnızca klinik risk alanlarında
  kullanılır.

## 5. Tasarım ve kullanılabilirlik iyileştirmeleri

Son cila turunda mevcut yapı korunarak şu noktalar uygulandı:

- Doktor listesindeki belirsiz “Bugün” başlığı, 24 saatlik pencereyi doğru anlatan
  **“Yaklaşan randevular”** başlığına çevrildi.
- Randevu listesi boş olduğunda iki adımlı **“Demo verisini sıfırla”** çağrısı eklendi.
- Doktor ekranındaki sağ panel başlığı sekmeye göre sadeleştirildi; tekrar eden “Modele giden veri”
  başlığı yerine **“AI denetimi”** kullanıldı.
- Aktif sekme renk, kalınlık ve daha güçlü alt çubukla belirginleştirildi.
- Tahlil tablosu kaydırılırken başlık satırı görünür kalır.
- Durum ve bağlantı sembolleri, erişilebilir metinleri korunarak ortak inline SVG ikon setine
  taşındı. Böylece emoji/font bağımlılığı azaltıldı.
- Boş, yükleniyor ve hata durumları ortak bileşenle ve 48 px hedef yüksekliğiyle gösterilir.

## 6. Doğrulama

Son doğrulama:

- Angular testleri: **15 test dosyası, 69 test başarılı**
- Angular production build: **başarılı**
- .NET testleri: **316 test başarılı**
- Angular production build: **başarılı**

PrimeUI lisansı bireysel demo kapsamına dahil edilmedi; uygulama lisans yapılandırması kullanılmadan
çalışır ve lisans uyarısı göstermez.

## 7. Bilinçli sınırlar

Bu demo gerçek kimlik doğrulama, gerçek e-Nabız entegrasyonu ve gerçek hasta verisi kullanmaz.
Ollama/eval dalı bu teslimata dahil edilmemiştir; görüşmede kapsamı büyütmek yerine güvenli AI
hattı, onay kapısı ve deterministik klinik karar mantığı gösterilmiştir.

## 8. İncelenebilecek bağlantılar

- [README ve demo senaryosu](../README.md)
- [Teknik şartname](./SPEC.md)
- [Mimari şema](./architecture.md)
- [Doktor ekranı görüntüsü](./img/01-doktor-on-anamnez.png)
- [Tahliller ekranı görüntüsü](./img/02-tahliller.png)
- [Hasta sonuçları görüntüsü](./img/03-hasta-sonuclar.png)
