import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { Rol } from './api';
import { Oturum } from './oturum';

/**
 * Rotayı yalnızca o roldeki oturuma açar; değilse girişe yönlendirir. TV ekranında adresteki bölüm
 * token'daki bölümle aynı olmalı. Asıl yetki kontrolü sunucudadır; bu yalnızca yanlış ekranı açmamak için.
 */
export function rolGuard(rol: Rol): CanMatchFn {
  return (_rota, parcalar) => {
    const oturum = inject(Oturum).aktif();
    const uygun =
      oturum?.rol === rol && (rol !== 'tv' || parcalar[1]?.path === oturum.kimlikId);
    return uygun || inject(Router).createUrlTree(['/giris']);
  };
}
