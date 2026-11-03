import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { HubConnectionState } from '@microsoft/signalr';
import { map, switchMap, timer } from 'rxjs';
import { fromBranch, trackBranch } from '../../core/live-queue';
import { Branch, Prescription } from '../../core/models';
import { PrescriptionsService } from '../../core/prescriptions.service';
import { PharmacyHubService } from '../../core/signalr/pharmacy-hub.service';

/** /display/:branchId — read-only waiting-room screen with the numbers that are ready. */
@Component({
  selector: 'app-display-screen',
  imports: [DatePipe],
  template: `
    <section class="min-h-[calc(100vh-64px)] bg-slate-950 px-6 py-8 text-slate-100">
      <header class="mx-auto flex max-w-6xl flex-wrap items-end justify-between gap-4">
        <div>
          <p class="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-400">
            Waiting room
          </p>
          <h1 class="mt-2 text-4xl font-bold">{{ branchName() }}</h1>
          <p class="text-sm text-slate-400">
            Ready prescriptions · <span class="font-mono">{{ branchId() }}</span>
          </p>
        </div>
        <div class="text-right">
          <p class="font-mono text-3xl font-bold text-emerald-300">{{ clock() | date: 'HH:mm:ss' }}</p>
          <p class="text-xs uppercase tracking-wide text-slate-500">Live</p>
        </div>
      </header>

      @if (joinError(); as message) {
        <p class="mx-auto mt-6 max-w-6xl rounded-xl bg-rose-500/15 px-4 py-3 text-sm font-medium text-rose-300">
          {{ message }}
        </p>
      }

      <div class="mx-auto mt-10 grid max-w-6xl gap-5 sm:grid-cols-2 lg:grid-cols-3">
        @for (prescription of ready(); track prescription.id) {
          <article
            class="rounded-3xl border border-emerald-500/30 bg-emerald-500/10 px-6 py-8 text-center shadow-[0_0_60px_-20px_rgba(16,185,129,0.6)]"
          >
            <p class="font-mono text-6xl font-black tracking-tight text-emerald-300 xl:text-7xl">
              {{ prescription.number }}
            </p>
            <p class="mt-3 text-sm font-semibold uppercase tracking-wide text-emerald-100">
              {{ prescription.patientInitials }}
            </p>
            <p class="mt-1 text-xs text-emerald-200/70">
              {{ prescription.items.length }} item(s) · ready
              {{ prescription.readyAt ? (prescription.readyAt | date: 'HH:mm') : '' }}
            </p>
          </article>
        } @empty {
          <div class="col-span-full rounded-3xl border border-dashed border-slate-700 px-6 py-20 text-center">
            <p class="text-lg font-semibold text-slate-300">No prescriptions are ready yet.</p>
            <p class="mt-2 text-sm text-slate-500">
              Numbers appear here the moment a pharmacist marks a prescription as Ready.
            </p>
          </div>
        }
      </div>
    </section>
  `,
})
export class DisplayScreen {
  private readonly hub = inject(PharmacyHubService);
  private readonly http = inject(PrescriptionsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly branchId = input.required<string>();

  protected readonly branches = toSignal(this.http.branches(), {
    initialValue: [] as Branch[],
  });
  protected readonly branchName = computed(
    () => this.branches().find((branch) => branch.id === this.branchId())?.name ?? this.branchId(),
  );

  protected readonly prescriptions = toSignal(
    toObservable(this.branchId).pipe(
      switchMap((branchId) => fromBranch(this.http, this.hub, branchId)),
    ),
    { initialValue: [] as Prescription[] },
  );

  protected readonly ready = computed(() =>
    this.prescriptions().filter((prescription) => prescription.status === 'Ready'),
  );

  protected readonly clock = toSignal(
    timer(0, 1_000).pipe(map(() => new Date())),
    { initialValue: new Date() },
  );

  protected readonly joinError = signal<string | null>(null);
  private activeBranch: string | null = null;

  constructor() {
    trackBranch(this.hub, toObservable(this.branchId), (message) => this.joinError.set(message))
      .pipe(takeUntilDestroyed())
      .subscribe((branchId) => {
        this.activeBranch = branchId;
        this.joinError.set(null);
      });

    this.destroyRef.onDestroy(() => {
      if (this.activeBranch && this.hub.state() === HubConnectionState.Connected) {
        this.hub.leaveBranch(this.activeBranch).subscribe({ error: () => undefined });
      }
    });
  }
}
