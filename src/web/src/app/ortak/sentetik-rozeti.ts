import { Component, input } from '@angular/core';

/** Her ekranda: verinin sentetik olduğunu gösterir (SPEC §6). */
@Component({
  selector: 'app-sentetik-rozeti',
  template: `<span
    class="inline-block rounded border px-1.5 py-0.5 font-semibold tracking-wide"
    [class]="koyu() ? 'border-slate-400 text-xl text-slate-200' : 'border-slate-500 text-xs text-slate-700'"
    title="Tüm veriler sentetiktir; gerçek hasta verisi yoktur"
    >SENTETİK</span
  >`,
})
export class SentetikRozeti {
  readonly koyu = input(false);
}
