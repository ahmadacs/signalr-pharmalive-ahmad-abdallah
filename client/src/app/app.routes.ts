import { Routes } from '@angular/router';
import { roleGuard } from './core/auth.service';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login').then((module) => module.Login),
  },
  {
    path: 'pharmacist',
    canActivate: [roleGuard('Pharmacist')],
    loadComponent: () =>
      import('./pages/placeholder/placeholder').then((module) => module.Placeholder),
  },
  {
    path: 'doctor',
    canActivate: [roleGuard('Doctor')],
    loadComponent: () =>
      import('./pages/placeholder/placeholder').then((module) => module.Placeholder),
  },
  {
    path: 'display',
    canActivate: [roleGuard('Display')],
    loadComponent: () =>
      import('./pages/placeholder/placeholder').then((module) => module.Placeholder),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
