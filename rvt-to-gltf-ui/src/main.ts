import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { App } from './app/app';

bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    provideZonelessChangeDetection(),
  ],
}).catch(console.error);
