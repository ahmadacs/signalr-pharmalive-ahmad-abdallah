import { Injectable, signal } from '@angular/core';
import { timer } from 'rxjs';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: string;
  message: string;
  kind: ToastKind;
}

/** Tiny app-wide toast queue. Each toast dismisses itself after 6 seconds. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly items = signal<Toast[]>([]);
  readonly toasts = this.items.asReadonly();

  show(message: string, kind: ToastKind = 'info'): void {
    const id = crypto.randomUUID();
    this.items.update((toasts) => [...toasts, { id, message, kind }]);

    timer(6_000).subscribe(() => this.dismiss(id));
  }

  dismiss(id: string): void {
    this.items.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }
}
