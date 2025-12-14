import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ConvertResponse {
  jobId: string;
  gltfUrl: string;
  binUrl?: string | null;
  zipUrl?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ConvertService {
  private readonly apiBase = 'http://localhost:5197';

  constructor(private http: HttpClient) {}

  convert(file: File): Observable<ConvertResponse> {
    const form = new FormData();
    form.append('rvtFile', file, file.name);

    return this.http.post<ConvertResponse>(`${this.apiBase}/convert`, form, {
      // debug’da network’te daha net görürsün
      observe: 'body'
    });
  }
}
