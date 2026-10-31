import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OutboxSatiri } from './api';

/** e-Nabız gönderim kutusu (simülasyon): yalnızca iş kuralı hatasındaki paket yeniden denenir (sunucu başka durumda 409). */
@Service()
export class OutboxIslemleri {
  private readonly http = inject(HttpClient);

  yenidenDene(id: string): Promise<OutboxSatiri> {
    return firstValueFrom(this.http.post<OutboxSatiri>(`/api/outbox/${id}/yeniden-dene`, null));
  }
}
