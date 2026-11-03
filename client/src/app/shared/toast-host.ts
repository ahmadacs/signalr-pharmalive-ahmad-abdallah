import { Component, inject } from '@angular/core';
import { ToastKind, ToastService } from '../core/toast.service';

@Component({
  selector: 'app-toast-host',
  template: `
    <div class="pointer-events-none fixed top-4 right-4 z-50 flex flex-col items-start gap-2 max-w-xs">

      @for (toast of toasts(); track toast.id) {
        <div
          class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-xl border px-4 py-3 text-sm shadow-xl"
          [class]="style(toast.kind)"
          role="status"
        >
          <span class="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-current"></span>
          <p class="flex-1 leading-snug">{{ toast.message }}</p>
          <button
            type="button"
            class="text-current/60 transition hover:text-current"
            aria-label="Dismiss"
            (click)="dismiss(toast.id)"
          >
            ✕
          </button>
        </div>
      }
    </div>
  `,
})
export class ToastHost {
  private readonly toastService = inject(ToastService);
  protected readonly toasts = this.toastService.toasts;

  protected dismiss(id: string): void {
    this.toastService.dismiss(id);
  }

  protected style(kind: ToastKind): string {
    switch (kind) {
      case 'success':
        return 'border-emerald-200 bg-emerald-50 text-emerald-900';
      case 'error':
        return 'border-rose-200 bg-rose-50 text-rose-900';
      default:
        return 'border-slate-200 bg-white text-slate-900';
    }
  }
}
