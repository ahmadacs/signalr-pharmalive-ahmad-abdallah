import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Branch } from '../../core/models';
import { PrescriptionsService } from '../../core/prescriptions.service';

/** /display — choose which branch the waiting-room screen should show. */
@Component({
  selector: 'app-display-picker',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-4xl px-4 py-12">
      <p class="text-xs font-semibold uppercase tracking-wide text-emerald-600">Waiting room</p>
      <h1 class="mt-1 text-3xl font-bold text-slate-900">Choose a branch</h1>
      <p class="mt-2 text-sm text-slate-500">
        The display screen shows the big numbers of prescriptions that are ready to collect.
      </p>

      <div class="mt-8 grid gap-4 sm:grid-cols-3">
        @for (branch of branches(); track branch.id) {
          <a
            class="group rounded-3xl border border-slate-200 bg-white p-6 shadow-sm transition hover:-translate-y-0.5 hover:border-emerald-400 hover:shadow-lg"
            [routerLink]="['/display', branch.id]"
          >
            <span class="font-mono text-sm font-bold text-emerald-600">{{ branch.id }}</span>
            <span class="mt-1 block text-xl font-bold text-slate-900">{{ branch.name }}</span>
            <span class="mt-4 inline-flex items-center gap-1 text-xs font-semibold text-slate-500 group-hover:text-emerald-700">
              Open screen →
            </span>
          </a>
        }
      </div>
    </section>
  `,
})
export class DisplayPicker {
  private readonly http = inject(PrescriptionsService);
  protected readonly branches = toSignal(this.http.branches(), {
    initialValue: [] as Branch[],
  });
}
