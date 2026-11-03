import {
  EMPTY,
  Observable,
  catchError,
  combineLatest,
  distinctUntilChanged,
  filter,
  ignoreElements,
  map,
  merge,
  pairwise,
  scan,
  startWith,
  switchMap,
} from 'rxjs';
import { Prescription } from './models';
import { PrescriptionsService } from './prescriptions.service';
import { hubErrorMessage } from './signalr/hub-client';
import { PharmacyHubService } from './signalr/pharmacy-hub.service';

/**
 * Initial queue over HTTP + every live change for that branch.
 *
 * The event stream is subscribed at the same time as the HTTP call, so a prescription
 * created while the request is in flight is buffered and merged, not lost.
 */
export function fromBranch(
  http: PrescriptionsService,
  hub: PharmacyHubService,
  branchId: string,
): Observable<Prescription[]> {
  // A reconnect can hide prescriptions created while we were offline, so the HTTP snapshot
  // is taken again and the live scan restarts from it.
  return hub.reconnected$.pipe(
    startWith(null),
    switchMap(() =>
      combineLatest([
        http.queue(branchId),
        liveChanges(hub, (prescription) => prescription.branchId === branchId),
      ]),
    ),
    map(([initial, changes]) => mergeById(initial, changes)),
  );
}

/** "My prescriptions" for a doctor: HTTP snapshot + private user updates. */
export function fromDoctor(
  http: PrescriptionsService,
  hub: PharmacyHubService,
  userName: string,
): Observable<Prescription[]> {
  return hub.reconnected$.pipe(
    startWith(null),
    switchMap(() =>
      combineLatest([
        http.mine(),
        liveChanges(hub, (prescription) => prescription.createdBy === userName),
      ]),
    ),
    map(([initial, changes]) => mergeById(initial, changes)),
  );
}

/**
 * Joins the new branch group and leaves the previous one.
 * Emits the branch id once the server accepted the join.
 */
export function trackBranch(
  hub: PharmacyHubService,
  branchIds: Observable<string>,
  onError: (message: string) => void,
): Observable<string> {
  return branchIds.pipe(
    startWith<string | null>(null),
    distinctUntilChanged(),
    pairwise(),
    switchMap(([previous, current]) => {
      if (!current) return EMPTY;

      // Leaving emits void; ignoreElements() keeps the merged stream typed as the branch id.
      const leave$ = previous
        ? hub.leaveBranch(previous).pipe(
            catchError(() => EMPTY),
            ignoreElements(),
          )
        : EMPTY;
      const join$ = hub.joinBranch(current).pipe(
        map(() => current),
        catchError((error: unknown) => {
          onError(hubErrorMessage(error));
          return EMPTY;
        }),
      );

      return merge(leave$, join$);
    }),
  );
}

export function mergeById(initial: Prescription[], updates: Prescription[]): Prescription[] {
  const byId = new Map(initial.map((prescription) => [prescription.id, prescription]));
  for (const update of updates) byId.set(update.id, update);

  return [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/**
 * A hot event stream folded into a Map, so the HTTP snapshot and the live events can be
 * combined with combineLatest(): events that arrive during the request are not lost.
 */
function liveChanges(
  hub: PharmacyHubService,
  predicate: (prescription: Prescription) => boolean,
): Observable<Prescription[]> {
  return merge(hub.prescriptionCreated$, hub.prescriptionUpdated$).pipe(
    filter(predicate),
    scan(
      (byId, prescription) => new Map(byId).set(prescription.id, prescription),
      new Map<string, Prescription>(),
    ),
    startWith(new Map<string, Prescription>()),
    map((byId) => [...byId.values()]),
  );
}
