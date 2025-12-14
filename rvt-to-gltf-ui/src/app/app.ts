import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConvertService, ConvertResponse } from './services/convert.service';
import { ViewerComponent } from './viewer/viewer.component';
import { finalize, timeout } from 'rxjs/operators';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, ViewerComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  selectedFile: File | null = null;
  loading = false;

  result: ConvertResponse | null = null;
  errorText = '';

  constructor(
    private convertService: ConvertService,
    private cdr: ChangeDetectorRef
  ) {}

  onPickFile(e: Event) {
    const input = e.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.result = null;
    this.errorText = '';
    this.cdr.detectChanges(); // ✅
  }

  convert() {
    if (!this.selectedFile || this.loading) return;

    this.loading = true;
    this.result = null;
    this.errorText = '';
    this.cdr.detectChanges(); // ✅ buton/spinner çizilsin

    this.convertService
      .convert(this.selectedFile)
      .pipe(
        timeout(15 * 60 * 1000),
        finalize(() => {
          this.loading = false;
          this.cdr.detectChanges(); // ✅ işlem bitince UI kesin güncellensin
        })
      )
      .subscribe({
        next: (res) => {
          console.log('NEXT RES', res); // debug
          this.result = res;
          this.cdr.detectChanges(); // ✅ success UI
        },
        error: (err) => {
          this.errorText =
            typeof err?.error === 'string'
              ? err.error
              : (err?.message ?? JSON.stringify(err));
          this.cdr.detectChanges(); // ✅ error UI
        }
      });
  }
}
