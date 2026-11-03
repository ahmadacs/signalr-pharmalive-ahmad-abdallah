import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, forkJoin, map, of, share, switchMap, tap } from 'rxjs';
import { BranchPresence, BranchStats, Prescription, PrescriptionStatus } from '../models';
import { HubClient } from './hub-client';

/**
 * The single RxJS entry point for the pharmacy hub.
 * Components never call connection.on(...) / connection.invoke(...) directly.
 */
@Injectable({ providedIn: 'root' })
export class PharmacyHubService extends HubClient {
  // Server -> client events. share() = one SignalR handler for many subscribers.
  readonly prescriptionCreated$ = this.on<Prescription>('PrescriptionCreated').pipe(share());
  readonly prescriptionUpdated$ = this.on<Prescription>('PrescriptionUpdated').pipe(share());
  readonly prescriptionReady$ = this.on<Prescription>('PrescriptionReady').pipe(share());
  readonly presence$ = this.on<BranchPresence>('PresenceChanged').pipe(share());

  private readonly joined = signal<ReadonlySet<string>>(new Set());
  readonly joinedBranches = this.joined.asReadonly();

  constructor() {
    super('/hubs/pharmacy', { statefulReconnect: true });

    // A reconnect normally means a new connection id -> group membership is gone.
    this.reconnected$
      .pipe(
        switchMap(() => this.rejoinAll()),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();
  }

  joinBranch(branchId: string): Observable<void> {
    return this.invoke('JoinBranch', branchId).pipe(
      tap(() =>
        this.joined.update((current) => {
          const next = new Set(current);
          next.add(branchId);
          return next;
        }),
      ),
    );
  }

  leaveBranch(branchId: string): Observable<void> {
    return this.invoke('LeaveBranch', branchId).pipe(
      tap(() =>
        this.joined.update((current) => {
          const next = new Set(current);
          next.delete(branchId);
          return next;
        }),
      ),
    );
  }

  claimPrescription(id: string): Observable<Prescription> {
    return this.invoke<Prescription>('ClaimPrescription', id);
  }

  updateStatus(id: string, status: PrescriptionStatus): Observable<Prescription> {
    return this.invoke<Prescription>('UpdateStatus', id, status);
  }

  /** Server -> client stream; unsubscribing (leaving the page) cancels it on the server. */
  streamBranchStats(branchId: string): Observable<BranchStats> {
    return this.stream<BranchStats>('StreamBranchStats', branchId);
  }

  override stop(): Observable<void> {
    return super.stop().pipe(tap(() => this.joined.set(new Set())));
  }

  private rejoinAll(): Observable<unknown> {
    const branches = [...this.joined()];
    if (branches.length === 0) return of(null);

    // One failing rejoin must not kill the reconnection pipeline.
    return forkJoin(
      branches.map((branchId) =>
        this.invoke('JoinBranch', branchId).pipe(catchError(() => of(void 0))),
      ),
    );
  }
}
