import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { AiKaynak } from '../cekirdek/api';

export interface HekimOnayi {
  hekimAd: string;
  zaman: string;
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
      [class.border-dashed]="!onay()"
      [class.border-solid]="!!onay()"
      [attr.aria-label]="baslik()"
      data-test="ai-karti"
    >
      <p class="text-sm font-medium text-ai" data-test="ai-baslik">
        {{ baslik() }} · <span class="tabular-nums">{{ zaman() | date: 'HH:mm' }}</span>
      </p>
      <div class="mt-2 text-slate-900">
        <ng-content />
      </div>
      @if (onay(); as o) {
        <p class="mt-3 text-sm font-medium text-ai" data-test="ai-onay">
          <span aria-hidden="true">✓</span> {{ o.hekimAd }} onayladı
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

  protected readonly baslik = computed(() => {
    switch (this.kaynak()) {
      case 'Llm':
        return `AI taslağı · klinik karar hekime aittir · ${this.model()}`;
      case 'Yedek':
        return 'Otomatik taslak · model yanıt vermedi, kural tabanlı';
      case 'Kural':
        return 'Otomatik taslak · kural tabanlı, model çağrılmadı';
    }
  });
}
