import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { DenetimSatiri } from '../cekirdek/api';

/** Kırmızı bayrak kuralının ekrandaki adı; denetim kaydında enum adı aynen kalır. */
export const KURAL_ETIKETI: Record<string, string> = {
  GogusAgrisiTerleme: 'Göğüs ağrısı + terleme',
  GogusAgrisi: 'Göğüs ağrısı',
  InmeBulgusu: 'İnme bulgusu',
  BilincKaybi: 'Bilinç kaybı',
  KendineZarar: 'Kendine zarar',
  GebelikKanama: 'Gebelikte kanama',
  BebekAtes: 'Bebekte ateş',
  AgirNefesDarligi: 'Ağır nefes darlığı',
};

/** Sağlayıcının ekrandaki adı; denetim kaydında yapılandırma adı (Gemini/Ollama/Fake/Kural) aynen kalır. */
export function saglayiciEtiketi(saglayici: string): string {
  switch (saglayici) {
    case 'Fake':
      return 'Çevrimdışı demo sağlayıcı';
    case 'Ollama':
      return 'Ollama (yerel)';
    case 'Kural':
      return 'Kural tabanlı';
    default:
      return saglayici;
  }
}

/**
 * "Modele giden veri" paneli (taslak 6, SPEC §9.2): modele giden maskeli metin aynen ve denetim kaydı.
 * Kural dalında modelin hiç çağrılmadığı, yedek/kota dalında nedeni açıkça yazar.
 */
@Component({
  selector: 'app-modele-giden-veri',
  imports: [DatePipe, DecimalPipe],
  template: `
    @let d = kayit();
    <div class="grid grid-cols-[minmax(0,1fr)_16rem] gap-4" data-test="modele-giden-veri">
      <div>
        <h3 class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Gönderilen istem (maskeli)</h3>
        <pre class="mt-1 rounded-md border border-slate-200 bg-slate-50 p-3 font-mono text-[13px] whitespace-pre-wrap text-slate-900 [overflow-wrap:anywhere]">{{ d.maskeliGirdi }}</pre>
        <p class="mt-2">
          @if (d.ozellik === 'TahlilAciklama') {
            <span class="text-basari" aria-hidden="true">✓ </span>Kimlik gönderilmedi · işaret kodla hesaplandı, model yalnızca cümle kurdu
          } @else if (d.ozellik === 'Epikriz') {
            <span class="text-basari" aria-hidden="true">✓ </span>Ad, TC, telefon maskelendi · yalnızca hekim onaylı ön anamnez gitti ·
            alerji ve tahlil satırları kodla eklendi
          } @else if (d.saglayici === 'Kural') {
            <span class="font-medium text-tehlike"><span aria-hidden="true">‼ </span>Kırmızı bayrak kuralı: {{ kuralEtiketi[d.neden ?? ''] ?? d.neden }} · model çağrılmadı</span>
          } @else {
            <span class="text-basari" aria-hidden="true">✓ </span>Kırmızı bayrak kuralı: tetiklenmedi
          }
        </p>
      </div>
      <div>
        <h3 class="text-xs font-semibold tracking-wide text-slate-600 uppercase">Denetim</h3>
        <dl class="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 tabular-nums">
          <dt class="text-slate-600">Model</dt>
          <dd data-test="denetim-model">{{ etiket(d.saglayici) }}{{ d.model ? ' / ' + d.model : '' }}</dd>
          <dt class="text-slate-600">İstem</dt><dd>{{ d.istemSurumu }}</dd>
          <dt class="text-slate-600">Süre</dt><dd>{{ d.gecikmeMs / 1000 | number: '1.1-1' }} sn</dd>
          <dt class="text-slate-600">Girdi hash</dt><dd [title]="d.girdiHash">{{ d.girdiHash.slice(0, 8) }}…</dd>
          <dt class="text-slate-600">Yedek</dt>
          <dd>{{ d.yedegeDustu ? 'evet' : 'hayır' }}</dd>
          @if (d.neden && d.saglayici !== 'Kural') {
            <dt class="text-slate-600">Neden</dt><dd class="text-uyari">{{ d.neden }}</dd>
          }
          <dt class="text-slate-600">Zaman</dt><dd>{{ d.zaman | date: 'HH:mm:ss' }}</dd>
        </dl>
      </div>
    </div>
  `,
})
export class ModeleGidenVeri {
  readonly kayit = input.required<DenetimSatiri>();
  protected readonly kuralEtiketi = KURAL_ETIKETI;
  protected readonly etiket = saglayiciEtiketi;
}
