import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Oturum } from './oturum';

/** /api isteklerine Bearer token ekler; 401'de (süresi dolmuş / geçersiz token) oturumu kapatıp girişe döner. */
export const yetkiInterceptor: HttpInterceptorFn = (istek, sonraki) => {
  const oturum = inject(Oturum);
  const router = inject(Router);
  const token = oturum.token();

  const giden =
    token && istek.url.startsWith('/api/')
      ? istek.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : istek;

  return sonraki(giden).pipe(
    catchError((hata: unknown) => {
      if (hata instanceof HttpErrorResponse && hata.status === 401 && token) {
        oturum.cikis();
        void router.navigate(['/giris']);
      }
      return throwError(() => hata);
    }),
  );
};
