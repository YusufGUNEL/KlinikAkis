import { Routes } from '@angular/router';
import { rolGuard } from './cekirdek/rol.guard';

// Rol başına lazy rota (SPEC §13.2). Yanlış roldeki oturum ya da oturumsuz erişim → /giris.
export const routes: Routes = [
  { path: 'giris', loadComponent: () => import('./giris/giris').then((m) => m.Giris), title: 'Giriş · KlinikAkış' },
  {
    path: 'hasta',
    canMatch: [rolGuard('hasta')],
    loadChildren: () => import('./hasta/hasta.routes').then((m) => m.hastaRotalari),
  },
  {
    path: 'doktor',
    canMatch: [rolGuard('doktor')],
    loadComponent: () => import('./doktor/doktor').then((m) => m.Doktor),
    title: 'Doktor · KlinikAkış',
  },
  {
    path: 'kabul',
    canMatch: [rolGuard('kabul')],
    loadComponent: () => import('./kabul/kabul').then((m) => m.Kabul),
    title: 'Kabul · KlinikAkış',
  },
  {
    path: 'tv/:bransId',
    canMatch: [rolGuard('tv')],
    loadComponent: () => import('./tv/tv').then((m) => m.Tv),
    title: 'Sıra ekranı · KlinikAkış',
  },
  { path: '', pathMatch: 'full', redirectTo: 'giris' },
  { path: '**', redirectTo: 'giris' },
];
