import { Routes } from '@angular/router';
import { HastaKabugu } from './hasta-kabugu';

// Hasta tarafı (telefon). Şikayet sohbeti, randevu ve sonuç ekranları sonraki aşamalarda buraya eklenir.
export const hastaRotalari: Routes = [
  {
    path: '',
    component: HastaKabugu,
    children: [
      { path: '', loadComponent: () => import('./hasta-ana').then((m) => m.HastaAna), title: 'Hasta · KlinikAkış' },
      {
        path: 'randevu-al',
        loadComponent: () => import('./randevu-al').then((m) => m.RandevuAl),
        title: 'Randevu al · KlinikAkış',
      },
    ],
  },
];
