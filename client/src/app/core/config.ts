import { InjectionToken } from '@angular/core';

/** Base URL of the ASP.NET Core API. Change here if you run the API on another port. */
export const API_URL = new InjectionToken<string>('API_URL', {
  providedIn: 'root',
  factory: () => 'http://localhost:5000',
});
