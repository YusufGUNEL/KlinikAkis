import { DestroyRef, inject, Signal, signal } from '@angular/core';

/**
 * Belirli aralıkla yenilenen "şimdi" sinyali (bileşen kurulurken çağrılır). Ekran gece yarısını geçerek
 * açık kalırsa "Bugün" başlığı ve TV saati kendiliğinden güncellenir.
 */
export function simdiSinyali(aralikMs = 30_000): Signal<Date> {
  const simdi = signal(new Date());
  const zamanlayici = setInterval(() => simdi.set(new Date()), aralikMs);
  inject(DestroyRef).onDestroy(() => clearInterval(zamanlayici));
  return simdi.asReadonly();
}
