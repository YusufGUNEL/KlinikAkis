import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { AiKaynak } from '../cekirdek/api';

export interface HekimOnayi {
  hekimAd: string;
  zaman: string;
  /** "onayladı" (varsayılan), "düzenledi", "reddetti". */
  eylem?: string;
  /** Reddedilen taslak onaylanmış sayılmaz: kenar kesik kalır, ✓ yerine ✕. */
  reddedildi?: boolean;
}

/**
 * Her makine taslağının kartı (SPEC §10.2): mor #6D28D9 + kesik kenar, LLM ve kural tabanlı yedek için
 * istisnasız. Hekim onaylayınca kenar düz olur ve "Dr. X onayladı 14:32" yazar. Güven yüzdesi yok.
 * İçerik ve Onayla/Düzenle/Reddet butonları dışarıdan (ng-content) gelir.
 */
@Component({
  selector: 'app-ai-karti',
  imports: [DatePipe],
  template: `
    <section
      class="rounded-lg border-2 border-ai bg-ai/5 p-4"
      [class.border-dashed]="!onaylandi()"
      [class.border-solid]="onaylandi()"
      [attr.aria-label]="baslik()"
      data-test="ai-karti"
    >
      <p class="text-sm font-medium text-ai" data-test="ai-baslik">
        {{ baslik() }} · <span class="tabular-nums">{{ zaman() | date: 'HH:mm' }}</span>
      </p>
      @if (kotaDoldu() && hedef() !== 'hasta') {
        <p class="mt-1 inline-block rounded border border-ai px-2 text-sm text-ai" data-test="ai-kota">
          demo kotası doldu · otomatik taslak
        </p>
      }
      <div class="mt-2 text-slate-900">
        <ng-content />
      </div>
      @if (onay(); as o) {
        <p class="mt-3 text-sm font-medium text-ai" data-test="ai-onay">
          <span aria-hidden="true">{{ o.reddedildi ? '✕' : '✓' }}</span> {{ o.hekimAd }} {{ o.eylem ?? 'onayladı' }}
          <span class="tabular-nums">{{ o.zaman | date: 'HH:mm' }}</span>
        </p>
      } @else {
        <div class="mt-3 flex flex-wrap gap-2">
          <ng-content select="[aiButonlar]" />
        </div>
      }
    </section>
  `,
})
export class AiKarti {
  readonly kaynak = input.required<AiKaynak>();
  /** Üretim zamanı (UTC ISO). */
  readonly zaman = input.required<string>();
  readonly model = input('');
  readonly onay = input<HekimOnayi | null>(null);
  /** Günlük kota / sağlayıcı kotası doldu: model çağrılmadı, taslak kural tabanlı (SPEC §9.9). */
  readonly kotaDoldu = input(false);
  /** Hasta ekranında başlık sade: model adı ve iç işleyiş yazmaz (taslak 2). */
  readonly hedef = input<'hekim' | 'hasta'>('hekim');

  protected readonly onaylandi = computed(() => !!this.onay() && !this.onay()!.reddedildi);

  protected readonly baslik = computed(() => {
    if (this.hedef() === 'hasta') return 'AI taslağı · tanı değildir';
    switch (this.kaynak()) {
      case 'Llm':
        return `AI taslağı · klinik karar hekime aittir · ${this.model()}`;
      case 'Yedek':
        return this.kotaDoldu()
          ? 'Otomatik taslak · model çağrılmadı (kota), kural tabanlı'
          : 'Otomatik taslak · model yanıt vermedi, kural tabanlı';
      case 'Kural':
        return 'Otomatik taslak · kural tabanlı, model çağrılmadı';
    }
  });
}
