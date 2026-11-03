import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { HubConnectionState } from '@microsoft/signalr';
import { Observable, catchError, filter, finalize, map, merge, of, startWith, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { hubErrorMessage } from '../../core/signalr/hub-client';
import { fromBranch, trackBranch } from '../../core/live-queue';
import {
  Branch,
  BranchStats,
  NEXT_STATUSES,
  Prescription,
  PrescriptionStatus,
  STATUSES,
} from '../../core/models';
import { PrescriptionsService } from '../../core/prescriptions.service';
import { PharmacyHubService } from '../../core/signalr/pharmacy-hub.service';

@Component({
  selector: 'app-pharmacist-board',
  imports: [DatePipe],
  templateUrl: './pharmacist.html',
})
export class PharmacistBoard {
  private readonly hub = inject(PharmacyHubService);
  private readonly http = inject(PrescriptionsService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = STATUSES;
  protected readonly userName = this.auth.userName;
  protected readonly connectionState = this.hub.state;
  protected readonly isConnected = computed(
    () => this.connectionState() === HubConnectionState.Connected,
  );

  protected readonly branches = toSignal(this.http.branches(), {
    initialValue: [] as Branch[],
  });
  protected readonly branchId = signal('RAM-01');
  protected readonly branchName = computed(
    () => this.branches().find((branch) => branch.id === this.branchId())?.name ?? this.branchId(),
  );

  /** Initial HTTP snapshot + live PrescriptionCreated/PrescriptionUpdated for this branch. */
  protected readonly prescriptions = toSignal(
    toObservable(this.branchId).pipe(
      switchMap((branchId) => fromBranch(this.http, this.hub, branchId)),
    ),
    { initialValue: [] as Prescription[] },
  );

  protected readonly pharmacists = toSignal(
    toObservable(this.branchId).pipe(
      switchMap((branchId) =>
        this.hub.presence$.pipe(
          filter((presence) => presence.branchId === branchId),
          map((presence) => presence.pharmacists),
          startWith([] as string[]),
        ),
      ),
    ),
    { initialValue: [] as string[] },
  );

  /** Server -> client stream. A drop kills it, so it restarts after every reconnect. */
  protected readonly stats = toSignal(
    toObservable(this.branchId).pipe(
      switchMap((branchId) =>
        merge(of(null), this.hub.reconnected$).pipe(
          switchMap(() =>
            this.hub.streamBranchStats(branchId).pipe(catchError(() => of(null))),
          ),
        ),
      ),
    ),
    { initialValue: null as BranchStats | null },
  );

  protected readonly columns = computed(() =>
    STATUSES.map((status) => ({
      status,
      items: this.prescriptions().filter((prescription) => prescription.status === status),
    })),
  );

  protected readonly pending = signal<ReadonlySet<string>>(new Set());
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly boardError = signal<string | null>(null);

  private activeBranch: string | null = null;

  constructor() {
    trackBranch(this.hub, toObservable(this.branchId), (message) =>
      this.boardError.set(message),
    )
      .pipe(takeUntilDestroyed())
      .subscribe((branchId) => {
        this.activeBranch = branchId;
        this.boardError.set(null);
      });

    // Do not leave a stale presence entry behind when the pharmacist leaves the page.
    this.destroyRef.onDestroy(() => {
      if (this.activeBranch) {
        this.hub.leaveBranch(this.activeBranch).subscribe({ error: () => undefined });
      }
    });
  }

  protected setBranch(branchId: string): void {
    this.branchId.set(branchId);
  }

  protected claim(prescription: Prescription): void {
    this.run(prescription.id, this.hub.claimPrescription(prescription.id));
  }

  protected move(prescription: Prescription, status: PrescriptionStatus): void {
    this.run(prescription.id, this.hub.updateStatus(prescription.id, status));
  }

  protected isMine(prescription: Prescription): boolean {
    return prescription.claimedBy !== null && prescription.claimedBy === this.userName();
  }

  protected canMove(prescription: Prescription): boolean {
    if (prescription.status === 'New') return false;
    if (prescription.claimedBy === null) return true;
    return this.isMine(prescription);
  }

  protected nextStatuses(prescription: Prescription): PrescriptionStatus[] {
    return (NEXT_STATUSES[prescription.status] ?? []).filter((status) => status !== 'Rejected');
  }

  protected canReject(prescription: Prescription): boolean {
    if (prescription.status === 'Dispensed' || prescription.status === 'Rejected') return false;
    return prescription.claimedBy === null || this.isMine(prescription);
  }

  protected isPending(prescription: Prescription): boolean {
    return this.pending().has(prescription.id);
  }

  protected errorFor(prescription: Prescription): string | undefined {
    return this.errors()[prescription.id];
  }

  protected formatSeconds(seconds: number | null): string {
    if (seconds === null) return '—';
    if (seconds < 60) return `${Math.round(seconds)}s`;

    return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
  }

  protected statusDot(status: PrescriptionStatus): string {
    switch (status) {
      case 'New':
        return 'bg-sky-500';
      case 'Claimed':
        return 'bg-violet-500';
      case 'Preparing':
        return 'bg-amber-500';
      case 'Ready':
        return 'bg-emerald-500';
      case 'Dispensed':
        return 'bg-slate-400';
      default:
        return 'bg-rose-500';
    }
  }

  protected moveLabel(status: PrescriptionStatus): string {
    return status === 'Dispensed' ? 'Dispense' : status;
  }

  /** One place that runs a hub action, tracks its pending state and surfaces its error on the card. */
  private run(id: string, action: Observable<unknown>): void {
    this.setPending(id, true);
    this.clearError(id);

    action
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.setPending(id, false)),
      )
      .subscribe({
        error: (error: unknown) => this.setError(id, hubErrorMessage(error)),
      });
  }

  private setPending(id: string, value: boolean): void {
    this.pending.update((current) => {
      const next = new Set(current);
      if (value) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  private setError(id: string, message: string): void {
    this.errors.update((errors) => ({ ...errors, [id]: message }));
  }

  private clearError(id: string): void {
    this.errors.update((errors) => {
      const next = { ...errors };
      delete next[id];
      return next;
    });
  }
}
