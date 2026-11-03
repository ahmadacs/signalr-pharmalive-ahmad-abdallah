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
      import('./pages/pharmacist/pharmacist').then((module) => module.PharmacistBoard),
  },
  {
    path: 'doctor',
    canActivate: [roleGuard('Doctor')],
    loadComponent: () => import('./pages/doctor/doctor').then((module) => module.Doctor),
  },
  {
    path: 'display',
    canActivate: [roleGuard('Display')],
    loadComponent: () =>
      import('./pages/display/display-picker').then((module) => module.DisplayPicker),
  },
  {
    path: 'display/:branchId',
    canActivate: [roleGuard('Display')],
    loadComponent: () =>
      import('./pages/display/display-screen').then((module) => module.DisplayScreen),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
