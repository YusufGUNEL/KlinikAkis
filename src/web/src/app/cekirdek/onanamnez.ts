import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CevapYaniti, OnAnamnezBaslatYaniti, OzetYaniti } from './api';

/** Şikayet sohbeti uçları. Kırmızı bayrak kararı sunucudaki kural motorundadır; burada yalnızca istek atılır. */
@Service()
export class OnAnamnezIslemleri {
  private readonly http = inject(HttpClient);

  baslat(): Promise<OnAnamnezBaslatYaniti> {
    return firstValueFrom(this.http.post<OnAnamnezBaslatYaniti>('/api/onanamnez', null));
  }

  cevapla(id: string, secimler: string[], metin: string | null): Promise<CevapYaniti> {
    return firstValueFrom(this.http.post<CevapYaniti>(`/api/onanamnez/${id}/cevap`, { secimler, metin }));
  }

  ozet(id: string): Promise<OzetYaniti> {
    return firstValueFrom(this.http.post<OzetYaniti>(`/api/onanamnez/${id}/ozet`, null));
  }
}
