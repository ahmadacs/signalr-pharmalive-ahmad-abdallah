import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_URL } from './config';
import { Role, TokenResponse } from './models';

const STORAGE_KEY = 'pharmalive.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = inject(API_URL);

  private readonly session = signal<TokenResponse | null>(restore());

  readonly token = computed(() => this.session()?.accessToken ?? null);
  readonly userName = computed(() => this.session()?.userName ?? null);
  readonly role = computed<Role | null>(() => this.session()?.role ?? null);
  readonly isLoggedIn = computed(() => this.token() !== null);

  login(userName: string, role: Role): Observable<TokenResponse> {
    return this.http
      .post<TokenResponse>(`${this.apiUrl}/api/auth/token`, { userName, role })
      .pipe(tap((response) => this.save(response)));
  }

  logout(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
  }

  private save(response: TokenResponse): void {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response));
    this.session.set(response);
  }
}

/** Adds the bearer token to normal HTTP calls; SignalR uses accessTokenFactory instead. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).token();
  return next(
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request,
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isLoggedIn() ? true : router.createUrlTree(['/login']);
};

export function roleGuard(...roles: Role[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isLoggedIn()) return router.createUrlTree(['/login']);

    const role = auth.role();
    return role && roles.includes(role) ? true : router.createUrlTree([homeFor(role)]);
  };
}

export function homeFor(role: Role | null): string {
  switch (role) {
    case 'Doctor':
      return '/doctor';
    case 'Pharmacist':
      return '/pharmacist';
    case 'Display':
      return '/display';
    default:
      return '/login';
  }
}

function restore(): TokenResponse | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;

    const session = JSON.parse(raw) as TokenResponse;
    return new Date(session.expiresAt).getTime() > Date.now() ? session : null;
  } catch {
    return null;
  }
}
