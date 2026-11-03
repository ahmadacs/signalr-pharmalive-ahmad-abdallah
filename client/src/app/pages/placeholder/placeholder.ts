import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { PharmacyHubService } from '../../core/signalr/pharmacy-hub.service';
import { ConnectionBadge } from '../../shared/connection-badge';

/**
 * Temporary landing page for Checkpoint 2: proves that login + hub connection + badge work.
 * Checkpoint 3 replaces it with the real pharmacist / doctor / display screens.
 */
@Component({
  selector: 'app-placeholder',
  imports: [ConnectionBadge],
  template: `
    <section class="mx-auto mt-10 max-w-xl rounded-3xl border border-slate-200 bg-white p-8 shadow-sm">
      <p class="text-xs font-semibold uppercase tracking-wide text-emerald-600">Checkpoint 2</p>
      <h1 class="mt-2 text-2xl font-bold text-slate-900">Hello {{ userName() }}</h1>
      <p class="mt-1 text-sm text-slate-500">
        You are signed in as <span class="font-semibold text-slate-700">{{ role() }}</span>.
        The hub service and the live connection badge are ready.
      </p>

      <div class="mt-6 flex items-center gap-3 rounded-2xl bg-slate-50 px-4 py-3">
        <span class="text-sm text-slate-600">SignalR</span>
        <app-connection-badge [state]="connectionState()" />
      </div>

      <p class="mt-6 text-sm leading-relaxed text-slate-500">
        The pharmacist board, the doctor workspace and the waiting-room display arrive in the next
        step.
      </p>
    </section>
  `,
})
export class Placeholder {
  private readonly auth = inject(AuthService);
  private readonly hub = inject(PharmacyHubService);

  protected readonly userName = this.auth.userName;
  protected readonly role = this.auth.role;
  protected readonly connectionState = this.hub.state;
}
