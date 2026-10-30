# KlinikAkış

Poliklinik akışı (randevu → kabul → muayene → tahlil → epikriz) + güvenli AI katmanı içeren demo.
Hasta ekranı telefonda, doktor ekranı dizüstünde; SignalR ile canlı senkron. Veri tamamen sentetik.

**Tüm gereksinimler, veri modeli, API, AI hattı, ekran taslakları ve aşamalar: `docs/SPEC.md`.**
Her oturumun başında SPEC.md'yi oku. SPEC ile çelişen bir şey yapmadan önce kullanıcıya sor.

## Çalışma şekli
- İş **aşama aşama** ilerler (SPEC §13 ve §13.N ayrıntıları). Kullanıcı "Aşama N" ya da `/asama N`
  der; sadece o aşamayı yap.
- Aşama başında: kalan günü SPEC §13'teki görüşme tarihine göre hesapla, önce planı yaz
  (dosyalar, kararlar, testler), kullanıcı onaylamadan kod yazma.
- Her aşamanın sonunda: derle, testleri koş, uygulamayı çalıştırıp ne görüldüğünü anlat,
  "Bitti" ölçütünü tek tek kontrol et. Kullanıcı onaylamadan commit atma ve sonraki aşamaya geçme.
- Kapsam dışına çıkma: SPEC'te olmayan özellik ekleme, "Yapılmayacak" listesindekileri hiç önerme.
- Kullanıcıya sade Türkçe anlat; her aşamada hangi kararı neden verdiğini 3-5 maddeyle yaz
  (kullanıcı bunu görüşmede savunacak).
- AI asistanın yanlış yaptığı ve düzeltildiği somut bir an olursa (uydurulan bölüm, İ/ı hatası,
  yanlış aralık vb.) `_calisma/AI_DUZELTMELER.md` dosyasına tarihli bir madde ekle.
- Kütüphane/SDK sürümü, API adı veya kurulum adımı konusunda emin değilsen hafızadan yazma;
  doğrula: .NET → `microsoft-learn` MCP, Angular → `angular-cli` MCP, PrimeNG → `primeng` MCP,
  diğer paketler → `context7` MCP, Claude API → `claude-api` skill'i.
- Paketler SPEC §3.1'deki kapalı listeden seçilir; listede olmayan paket için önce sor.
- Görev dağılımı: aşama sonunda `.claude/agents/` altındaki denetçiler (`spec-denetci`,
  `ai-guvenlik-denetci`, `turkce-denetci`, `ui-denetci`) paralel çalıştırılır; kodu ana oturum yazar.

## Değişmez kurallar
- **Karar kodda, cümle modelde:** H/L işareti, bölüm listesi, kırmızı bayrak deterministik koddadır.
  LLM'e asla sınıflandırma kararı verdirme.
- Her LLM çağrısı tek `AiHatti` üzerinden: maskeleme → kural kontrolü → kota → `IChatClient`
  (JSON şema) → sunucu doğrulaması → hata/zaman aşımında yedek → `AiAuditLog`.
- AI çıktısı onaylanmadan klinik kayda ve hastaya görünen alana yazılmaz; arayüzde mor `#6D28D9`
  + kesik çizgili kartla gösterilir (kural tabanlı yedek de).
- Alerji alanı her zaman görünür; boşsa "Bilinen alerji yok".
- Kırmızı `#B91C1C` yalnızca alerji, kritik değer, kırmızı bayrak için.
- Randevu durumu yalnızca Domain'deki durum makinesiyle değişir (SPEC §5.1).
- SignalR grupları sunucuda claim'lerden atanır; payload yalnızca `{tur, id}`.
- API anahtarı ve bağlantı dizesi asla repoya girmez (ortam değişkeni / user-secrets).
- Türkçe yerel ayar (SPEC §10.5): `lang="tr"`, `29.10.2026`, ondalık virgül; Angular `uppercase`
  pipe yerine `trUpper`; sunucu `InvariantCulture`, Türkçe eşleşme yalnızca `TurkceMetin` içinde.
- Zamanlar UTC `DateTime`; "bugün" Europe/Istanbul'a göre. Docker imajı Ubuntu `aspnet:10.0-noble` (ICU + tzdata; Alpine değil — .NET 10'da Debian imajı yok).
- Claude API kullanırken model kimliğini ve C# SDK'nın `IChatClient` desteğini önce `claude-api`
  skill'i / resmi dokümandan doğrula; hafızadan yazma.

## Komutlar (Aşama 0'dan sonra güncellenecek)
- Backend: `dotnet run --project src/KlinikAkis.Api`
- Frontend: `cd src/web && npm start`
- Test: `dotnet test`

## Repo hijyeni
- `_calisma/` klasörü kişisel çalışma notlarıdır (promptlar, görüşme hazırlığı); `.gitignore`'dadır,
  asla commit edilmez. `.claude/` (kişisel slash komutları) da ignore'dadır.
- Commit öncesi `git status` oku, dosyaları açıkça stage et; `git add -A` kullanma.
