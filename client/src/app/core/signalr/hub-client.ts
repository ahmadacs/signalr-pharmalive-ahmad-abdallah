import { inject, isDevMode } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import {
  BehaviorSubject,
  Observable,
  catchError,
  defer,
  filter,
  from,
  map,
  of,
  retry,
  Subject,
  switchMap,
  take,
  tap,
  throwError,
  timer,
} from 'rxjs';
import { AuthService } from '../auth.service';
import { API_URL } from '../config';

export interface HubClientOptions {
  /** .NET 8+ stateful reconnect: a short drop resumes the same connection. */
  statefulReconnect?: boolean;
}

/**
 * Thin, reusable RxJS wrapper around a SignalR HubConnection.
 *
 *  - on<T>()     server -> client events as Observables (handler removed on unsubscribe)
 *  - invoke<T>() client -> server call that waits for the result (Observable)
 *  - send()      fire-and-forget client -> server call
 *  - stream<T>() server -> client stream as an Observable (disposed on unsubscribe)
 *  - state       connection state as a Signal (components never touch the connection)
 */
export abstract class HubClient {
  private readonly auth = inject(AuthService);
  private readonly apiUrl = inject(API_URL);

  protected readonly connection: HubConnection;

  private readonly stateSubject = new BehaviorSubject<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  private readonly reconnectedSubject = new Subject<string | undefined>();

  readonly state$ = this.stateSubject.asObservable();
  readonly state = toSignal(this.state$, { requireSync: true });

  /** Emits after an automatic reconnect. Groups are bound to a connection id, so rejoin here. */
  readonly reconnected$ = this.reconnectedSubject.asObservable();

  protected constructor(hubPath: string, options: HubClientOptions = {}) {
    let builder = new HubConnectionBuilder()
      .withUrl(`${this.apiUrl}${hubPath}`, {
        // Called on every (re)connect, so a refreshed token is always used.
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .withServerTimeout(30_000)
      .withKeepAliveInterval(15_000)
      .configureLogging(isDevMode() ? LogLevel.Information : LogLevel.Warning);

    if (options.statefulReconnect) builder = builder.withStatefulReconnect();

    this.connection = builder.build();

    this.connection.onreconnecting(() => this.stateSubject.next(HubConnectionState.Reconnecting));
    this.connection.onreconnected((connectionId) => {
      this.stateSubject.next(HubConnectionState.Connected);
      this.reconnectedSubject.next(connectionId);
    });
    this.connection.onclose(() => this.stateSubject.next(HubConnectionState.Disconnected));
  }

  /**
   * Starts the connection. withAutomaticReconnect() does NOT cover the first start,
   * so it is retried here with exponential back-off.
   */
  start(): Observable<void> {
    if (this.connection.state !== HubConnectionState.Disconnected) return of(void 0);

    this.stateSubject.next(HubConnectionState.Connecting);

    return defer(() => this.connection.start()).pipe(
      retry({
        count: 5,
        delay: (_error, attempt) => timer(Math.min(1_000 * 2 ** attempt, 15_000)),
      }),
      tap(() => this.stateSubject.next(HubConnectionState.Connected)),
      catchError((error) => {
        this.stateSubject.next(HubConnectionState.Disconnected);
        return throwError(() => error);
      }),
    );
  }

  stop(): Observable<void> {
    return defer(() => this.connection.stop()).pipe(
      tap(() => this.stateSubject.next(HubConnectionState.Disconnected)),
    );
  }

  /** Server -> client event. Subscribe BEFORE start() so early messages are not lost. */
  protected on<T>(eventName: string): Observable<T> {
    return new Observable<T>((subscriber) => {
      const handler = (payload: T) => subscriber.next(payload);
      this.connection.on(eventName, handler);
      return () => this.connection.off(eventName, handler);
    });
  }

  /** Client -> server call that waits for the hub method's return value (or HubException). */
  protected invoke<T = void>(methodName: string, ...args: unknown[]): Observable<T> {
    return this.whenConnected().pipe(
      switchMap(() => defer(() => from(this.connection.invoke<T>(methodName, ...args)))),
    );
  }

  /** Client -> server call without waiting for the server to finish. */
  protected send(methodName: string, ...args: unknown[]): Observable<void> {
    return this.whenConnected().pipe(
      switchMap(() => defer(() => from(this.connection.send(methodName, ...args)))),
    );
  }

  /** Server -> client stream (IAsyncEnumerable on the server). Unsubscribing cancels it. */
  protected stream<T>(methodName: string, ...args: unknown[]): Observable<T> {
    return this.whenConnected().pipe(
      switchMap(
        () =>
          new Observable<T>((subscriber) => {
            const subscription = this.connection.stream<T>(methodName, ...args).subscribe({
              next: (value) => subscriber.next(value),
              error: (error) => subscriber.error(error),
              complete: () => subscriber.complete(),
            });

            return () => subscription.dispose();
          }),
      ),
    );
  }

  /**
   * Blocks until the connection is usable. A page reload mounts components before
   * start() has finished, so an unguarded invoke would fail with
   * "Cannot send data if the connection is not in the 'Connected' State."
   */
  private whenConnected(): Observable<void> {
    return this.state$.pipe(
      filter((state) => state === HubConnectionState.Connected),
      take(1),
      map(() => void 0),
    );
  }
}

/** Reads the readable part of a SignalR error ("...HubException: <message>"). */
export function hubErrorMessage(error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error);
  const marker = 'HubException: ';
  const index = raw.indexOf(marker);
  return index >= 0 ? raw.slice(index + marker.length) : raw;
}
