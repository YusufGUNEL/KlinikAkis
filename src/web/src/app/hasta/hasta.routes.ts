import { Routes } from '@angular/router';
import { HastaKabugu } from './hasta-kabugu';

// Hasta tarafı (telefon): randevularım, şikayet sohbeti → randevu. Sonuç ekranı Aşama 6'da eklenir.
export const hastaRotalari: Routes = [
  {
    path: '',
    component: HastaKabugu,
    children: [
      { path: '', loadComponent: () => import('./hasta-ana').then((m) => m.HastaAna), title: 'Hasta · KlinikAkış' },
      {
        path: 'sikayet',
        loadComponent: () => import('./sikayet').then((m) => m.Sikayet),
        title: 'Şikayetiniz · KlinikAkış',
      },
      {
        path: 'randevu-al',
        loadComponent: () => import('./randevu-al').then((m) => m.RandevuAl),
        title: 'Randevu al · KlinikAkış',
      },
    ],
  },
];
