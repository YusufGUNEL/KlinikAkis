import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { Oturum } from '../cekirdek/oturum';
import { SentetikRozeti } from '../ortak/sentetik-rozeti';

/** Mobil kabuk (390 px hedef): gövde 18 px, dokunma hedefleri ≥ 48 px (SPEC §10.1). */
@Component({
  selector: 'app-hasta-kabugu',
  imports: [RouterOutlet, SentetikRozeti],
  template: `
    <div class="mx-auto flex min-h-dvh max-w-md flex-col bg-white text-[18px] leading-relaxed">
      <header class="flex items-center justify-between gap-2 border-b border-slate-200 px-4 py-2">
        <span class="font-semibold text-ana">KlinikAkış</span>
        <span class="flex items-center gap-2">
          <app-sentetik-rozeti />
          <button
            type="button"
            class="min-h-12 rounded-lg px-3 text-base font-medium text-slate-700 hover:bg-slate-100"
            (click)="cikis()"
          >
            Çıkış
          </button>
        </span>
      </header>
      <main class="flex-1 px-4 py-5">
        <router-outlet />
      </main>
    </div>
  `,
})
export class HastaKabugu {
  private readonly oturum = inject(Oturum);
  private readonly router = inject(Router);

  protected cikis(): void {
    this.oturum.cikis();
    void this.router.navigate(['/giris']);
  }
}
