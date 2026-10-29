import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

interface SaglikYaniti {
  durum: string;
  zaman: string;
}

// Aşama 0 geçici sayfası: proxy üzerinden /api/health. Aşama 2'de rol seçiciyle değişir.
@Component({
  imports: [RouterOutlet, DatePipe],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  protected readonly saglik = httpResource<SaglikYaniti>(() => '/api/health');
}
