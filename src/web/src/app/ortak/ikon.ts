import { Component, input } from '@angular/core';

export type IkonAdi =
  | 'bos'
  | 'yukleniyor'
  | 'hata'
  | 'canli'
  | 'baglaniyor'
  | 'koptu'
  | 'planlandi'
  | 'kabul'
  | 'cagrildi'
  | 'muayenede'
  | 'tamamlandi'
  | 'iptal'
  | 'gelmedi'
  | 'uyari'
  | 'bekliyor'
  | 'hasta'
  | 'doktor'
  | 'tv';

@Component({
  selector: 'app-ikon',
  template: `
    <svg class="inline-block h-[1em] w-[1em] shrink-0 align-[-0.12em]" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      @switch (adi()) {
        @case ('bos') { <circle cx="12" cy="12" r="8" /> }
        @case ('yukleniyor') { <path d="M12 5v4m0 6v4M5 12h4m6 0h4" /> }
        @case ('hata') { <circle cx="12" cy="12" r="8" /><path d="m9 9 6 6m0-6-6 6" /> }
        @case ('canli') { <circle cx="12" cy="12" r="4" fill="currentColor" stroke="none" /> }
        @case ('baglaniyor') { <path d="M12 5v2m0 10v2M5 12h2m10 0h2" /> }
        @case ('koptu') { <circle cx="12" cy="12" r="8" /> }
        @case ('planlandi') { <circle cx="12" cy="12" r="7" /> }
        @case ('kabul') { <circle cx="12" cy="12" r="7" fill="currentColor" stroke="none" /> }
        @case ('cagrildi') { <path d="M5 12h13m-5-5 5 5-5 5" /> }
        @case ('muayenede') { <path d="m8 5 9 7-9 7z" fill="currentColor" stroke="none" /> }
        @case ('tamamlandi') { <path d="m5 12 4 4L19 6" /> }
        @case ('iptal') { <circle cx="12" cy="12" r="8" /><path d="m8 8 8 8" /> }
        @case ('gelmedi') { <circle cx="12" cy="12" r="8" /><path d="m9 9 6 6m0-6-6 6" /> }
        @case ('uyari') { <path d="m12 4 9 16H3z" /><path d="M12 9v4m0 3h.01" /> }
        @case ('bekliyor') { <circle cx="12" cy="12" r="8" /><path d="M12 8v4l3 2" /> }
        @case ('hasta') { <circle cx="12" cy="8" r="3" /><path d="M5 20c.8-4 3-6 7-6s6.2 2 7 6" /> }
        @case ('doktor') { <path d="M8 4h8v5a4 4 0 0 1-8 0z" /><path d="M12 13v7m-4 0h8" /> }
        @case ('tv') { <rect x="4" y="6" width="16" height="12" rx="1" /><path d="m9 3 3 3 3-3" /> }
      }
    </svg>
  `,
})
export class Ikon {
  readonly adi = input.required<IkonAdi>();
}
