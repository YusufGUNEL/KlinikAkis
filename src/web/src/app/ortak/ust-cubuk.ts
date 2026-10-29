import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { Oturum } from '../cekirdek/oturum';
import { SentetikRozeti } from './sentetik-rozeti';

/** Doktor ve kabul ekranlarının üst çubuğu: ekran adı, oturumdaki kişi, SENTETİK, çıkış. */
@Component({
  selector: 'app-ust-cubuk',
  imports: [SentetikRozeti],
  template: `
    <header class="flex h-12 items-center justify-between gap-4 border-b border-slate-200 bg-white px-4">
      <p class="flex items-center gap-2">
        <span class="font-semibold text-ana">KlinikAkış</span>
        <span class="text-slate-400" aria-hidden="true">·</span>
        <span class="font-medium text-slate-900">{{ ekran() }}</span>
        <app-sentetik-rozeti />
      </p>
      <p class="flex items-center gap-3">
        <ng-content />
        <span class="text-slate-700">{{ oturum.aktif()?.ad }}</span>
        <button type="button" class="rounded px-2 py-1 font-medium text-slate-700 hover:bg-slate-100" (click)="cikis()">
          Çıkış
        </button>
      </p>
    </header>
  `,
})
export class UstCubuk {
  readonly ekran = input.required<string>();

  protected readonly oturum = inject(Oturum);
  private readonly router = inject(Router);

  protected cikis(): void {
    this.oturum.cikis();
    void this.router.navigate(['/giris']);
  }
}
