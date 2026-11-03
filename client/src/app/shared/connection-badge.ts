import { Component, computed, input } from '@angular/core';
import { HubConnectionState } from '@microsoft/signalr';

/** Connected / Connecting / Reconnecting / Disconnected, as a small pill. */
@Component({
  selector: 'app-connection-badge',
  template: `
    <span
      class="inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-semibold"
      [class]="style().box"
      [attr.title]="'SignalR connection: ' + label()"
    >
      <span class="h-2 w-2 rounded-full" [class]="style().dot"></span>
      {{ label() }}
    </span>
  `,
})
export class ConnectionBadge {
  readonly state = input.required<HubConnectionState>();

  protected readonly label = computed(() => {
    switch (this.state()) {
      case HubConnectionState.Connected:
        return 'Connected';
      case HubConnectionState.Connecting:
        return 'Connecting';
      case HubConnectionState.Reconnecting:
        return 'Reconnecting';
      case HubConnectionState.Disconnecting:
        return 'Disconnecting';
      default:
        return 'Disconnected';
    }
  });

  protected readonly style = computed(() => {
    switch (this.state()) {
      case HubConnectionState.Connected:
        return { box: 'bg-emerald-100 text-emerald-800', dot: 'bg-emerald-500' };
      case HubConnectionState.Connecting:
      case HubConnectionState.Reconnecting:
        return { box: 'bg-amber-100 text-amber-800', dot: 'bg-amber-500 animate-pulse' };
      case HubConnectionState.Disconnecting:
        return { box: 'bg-slate-200 text-slate-700', dot: 'bg-slate-500' };
      default:
        return { box: 'bg-rose-100 text-rose-800', dot: 'bg-rose-500' };
    }
  });
}
