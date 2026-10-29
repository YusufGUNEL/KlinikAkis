import { Pipe, PipeTransform } from '@angular/core';

/**
 * Türkçe büyük harf: "endokrinoloji" → "ENDOKRİNOLOJİ", "ılık" → "ILIK".
 * Angular'ın `uppercase` pipe'ı yerel ayarı bilmez ("i" → "I"); projede kullanılmaz (SPEC §10.5).
 */
@Pipe({ name: 'trUpper' })
export class TrUpperPipe implements PipeTransform {
  transform(deger: string | null | undefined): string {
    return deger?.toLocaleUpperCase('tr-TR') ?? '';
  }
}
