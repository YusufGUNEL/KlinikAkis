import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SeedSayimlari } from './api';

@Service()
export class Demo {
  private readonly http = inject(HttpClient);

  /** POST /api/demo/reset: seed durumuna döner, slotlar şimdiye göre yeniden üretilir (SPEC §5.3). */
  sifirla(): Promise<SeedSayimlari> {
    return firstValueFrom(this.http.post<SeedSayimlari>('/api/demo/reset', null));
  }
}
