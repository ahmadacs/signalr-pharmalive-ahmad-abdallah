import { Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { PharmacyHubService } from './core/signalr/pharmacy-hub.service';
import { ConnectionBadge } from './shared/connection-badge';
import { ToastHost } from './shared/toast-host';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ConnectionBadge, ToastHost],
  templateUrl: './app.html',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly hub = inject(PharmacyHubService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly isLoggedIn = this.auth.isLoggedIn;
  protected readonly userName = this.auth.userName;
  protected readonly role = this.auth.role;
  protected readonly connectionState = this.hub.state;

  constructor() {
    // One place owns the connection lifecycle: log in -> start, log out -> stop.
    effect(() => {
      const loggedIn = this.auth.isLoggedIn();

      untracked(() => {
        if (loggedIn) {
          this.hub
            .start()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ error: () => undefined });
        } else {
          this.hub
            .stop()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ error: () => undefined });
        }
      });
    });
  }

  protected logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
