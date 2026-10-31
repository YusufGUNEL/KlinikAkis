import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CevapYaniti,
  HekimKararTuru,
  OnAnamnezBaslatYaniti,
  OnAnamnezDetayi,
  HastaOzetKarti,
  OzetYaniti,
} from './api';

/** Şikayet sohbeti ve ön anamnez kararı uçları. Kırmızı bayrak kararı sunucudaki kural motorundadır; burada yalnızca istek atılır. */
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

  /** Hasta özeti doğrular (Doğru) ya da not ekler (Düzelt); taslak değişmez, not hekime gider. */
  hastaOnayi(id: string, dogru: boolean, not: string | null): Promise<HastaOzetKarti> {
    return firstValueFrom(this.http.post<HastaOzetKarti>(`/api/onanamnez/${id}/hasta-onayi`, { dogru, not }));
  }

  /** Hekim kararı (SPEC §10.2). */
  karar(id: string, karar: HekimKararTuru, metin: string | null): Promise<OnAnamnezDetayi> {
    return firstValueFrom(this.http.post<OnAnamnezDetayi>(`/api/onanamnez/${id}/karar`, { karar, metin }));
  }
}
