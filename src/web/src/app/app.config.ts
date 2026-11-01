import { DATE_PIPE_DEFAULT_OPTIONS, registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import localeTr from '@angular/common/locales/tr';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { yetkiInterceptor } from './cekirdek/yetki.interceptor';
import { KlinikPreset } from './tema';

registerLocaleData(localeTr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Rota ve sorgu parametreleri bileşen input'larına bağlanır (/tv/:bransId, /giris?rol=&kimlik=).
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([yetkiInterceptor])),
    { provide: LOCALE_ID, useValue: 'tr' },
    // DatePipe IANA adını ('Europe/Istanbul') tanımaz, sessizce tarayıcı dilimine düşer.
    // Türkiye 2016'dan beri yaz saati uygulamıyor: sabit +03:00.
    { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { timezone: '+0300', dateFormat: 'dd.MM.yyyy' } },
    providePrimeNG({
      theme: {
        preset: KlinikPreset,
        options: {
          darkModeSelector: false,
          // PrimeNG katmanı Tailwind'in theme/base katmanlarından sonra, utilities'ten önce.
          cssLayer: { name: 'primeng', order: 'theme, base, primeng' },
        },
      },
    }),
  ],
};
