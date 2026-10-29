// PrimeUI lisans anahtarını repoya koymadan derlemeye verir.
// Kaynak sırası: PRIMEUI_LICENSE ortam değişkeni → src/web/.primeui-license dosyası → boş.
// Çıktı: src/app/lisans.ts (git'e girmez).
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const kok = join(dirname(fileURLToPath(import.meta.url)), '..');
const dosya = join(kok, '.primeui-license');

const anahtar = (
  process.env.PRIMEUI_LICENSE ?? (existsSync(dosya) ? readFileSync(dosya, 'utf8') : '')
).trim();

if (!anahtar) {
  console.warn('[lisans-yaz] PrimeUI lisans anahtarı bulunamadı; sayfada lisans uyarısı görünecek.');
}

writeFileSync(
  join(kok, 'src', 'app', 'lisans.ts'),
  `// Otomatik üretilir (scripts/lisans-yaz.mjs); düzenlemeyin, commit etmeyin.\nexport const PRIMEUI_LISANS = ${JSON.stringify(anahtar)};\n`,
);
