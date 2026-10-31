import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HekimKararTuru, MuayeneDetayi } from './api';

/** Hekim notu ve epikriz uçları. Epikriz AiHatti'den geçer; alerji/tahlil satırları sunucuda kodla kurulur. */
@Service()
export class MuayeneIslemleri {
  private readonly http = inject(HttpClient);

  notKaydet(muayeneId: string, metin: string | null): Promise<MuayeneDetayi> {
    return firstValueFrom(this.http.put<MuayeneDetayi>(`/api/muayeneler/${muayeneId}/not`, { metin }));
  }

  /** Bekleyen taslak varsa sunucu modeli yeniden çağırmaz, onu döner. */
  epikrizUret(muayeneId: string): Promise<MuayeneDetayi> {
    return firstValueFrom(this.http.post<MuayeneDetayi>(`/api/muayeneler/${muayeneId}/epikriz`, null));
  }

  karar(epikrizId: string, karar: HekimKararTuru, metin: string | null): Promise<MuayeneDetayi> {
    return firstValueFrom(this.http.post<MuayeneDetayi>(`/api/epikriz/${epikrizId}/karar`, { karar, metin }));
  }
}
