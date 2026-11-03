import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { fromDoctor } from '../../core/live-queue';
import { Branch, Prescription, PrescriptionStatus, ValidationProblem } from '../../core/models';
import { PrescriptionsService } from '../../core/prescriptions.service';
import { PharmacyHubService } from '../../core/signalr/pharmacy-hub.service';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-doctor',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './doctor.html',
})
export class Doctor {
  private readonly hub = inject(PharmacyHubService);
  private readonly http = inject(PrescriptionsService);
  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  protected readonly userName = this.auth.userName;
  protected readonly branches = toSignal(this.http.branches(), {
    initialValue: [] as Branch[],
  });

  /** Initial /mine snapshot + private PrescriptionCreated/PrescriptionUpdated for this doctor. */
  protected readonly prescriptions = toSignal(
    fromDoctor(this.http, this.hub, this.auth.userName() ?? ''),
    { initialValue: [] as Prescription[] },
  );

  protected readonly form = this.fb.nonNullable.group({
    number: ['', [Validators.required]],
    branchId: ['RAM-01', [Validators.required]],
    patientInitials: ['', [Validators.required]],
    items: this.fb.nonNullable.array([this.fb.nonNullable.control('', [Validators.required])]),
  });

  protected readonly submitting = signal(false);
  protected readonly requestError = signal<string | null>(null);
  private readonly serverErrors = signal<Record<string, string[]>>({});

  protected readonly counts = computed(() => ({
    total: this.prescriptions().length,
    ready: this.prescriptions().filter((prescription) => prescription.status === 'Ready').length,
  }));

  constructor() {
    this.refreshNextNumber();

    // Private notification: the server sends this only to the doctor who created it.
    this.hub.prescriptionReady$
      .pipe(takeUntilDestroyed())
      .subscribe((prescription) =>
        this.toasts.show(
          `${prescription.number} is ready — ${prescription.patientInitials}.`,
          'success',
        ),
      );
  }

  protected get itemsArray() {
    return this.form.controls.items;
  }

  protected addItem(): void {
    this.form.controls.items.push(this.fb.nonNullable.control('', [Validators.required]));
  }

  protected removeItem(index: number): void {
    if (this.form.controls.items.length > 1) this.form.controls.items.removeAt(index);
  }

  protected controlError(name: 'number' | 'branchId' | 'patientInitials'): string | null {
    const server = this.serverErrors()[name]?.[0];
    if (server) return server;

    const control = this.form.controls[name];
    if (!control.touched || control.valid) return null;

    return name === 'patientInitials'
      ? 'Patient initials are required, e.g. A.K.'
      : name === 'branchId'
        ? 'Choose a branch.'
        : 'Prescription number is required, e.g. RX-1024.';
  }

  protected itemError(index: number): string | null {
    const server = this.serverErrors()[`items[${index}]`]?.[0];
    if (server) return server;

    const control = this.form.controls.items.at(index);
    if (!control || !control.touched || control.valid) return null;

    return `Item ${index + 1} is required.`;
  }

  protected itemsError(): string | null {
    return this.serverErrors()['items']?.[0] ?? null;
  }

  protected submit(): void {
    if (this.submitting()) return;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const items = value.items.map((item) => item.trim()).filter((item) => item.length > 0);
    if (items.length === 0) {
      this.serverErrors.set({ items: ['At least one medicine item is required.'] });
      return;
    }

    this.submitting.set(true);

    this.requestError.set(null);
    this.serverErrors.set({});

    this.http
      .create({
        number: value.number.trim(),
        branchId: value.branchId,
        patientInitials: value.patientInitials.trim(),
        items,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (prescription) => {
          this.submitting.set(false);
          this.toasts.show(
            `${prescription.number} created for ${prescription.patientInitials} in ${prescription.branchId}.`,
            'success',
          );
          this.resetForm(prescription.branchId);
        },
        error: (error: unknown) => {
          this.submitting.set(false);

          const body = (error as { error?: ValidationProblem })?.error;
          if (body?.errors) {
            this.serverErrors.set(body.errors);
            return;
          }

          const status = (error as { status?: number })?.status;
          this.requestError.set(
            status === 0
              ? 'Cannot reach the API on http://localhost:5000. Start the server and try again.'
              : `Creating the prescription failed (HTTP ${status ?? 'unknown'}). Please try again.`,
          );
        },
      });
  }

  protected branchName(branchId: string): string {
    return this.branches().find((branch) => branch.id === branchId)?.name ?? branchId;
  }

  protected statusClass(status: PrescriptionStatus): string {
    switch (status) {
      case 'New':
        return 'bg-sky-100 text-sky-700';
      case 'Claimed':
        return 'bg-violet-100 text-violet-700';
      case 'Preparing':
        return 'bg-amber-100 text-amber-700';
      case 'Ready':
        return 'bg-emerald-100 text-emerald-700';
      case 'Dispensed':
        return 'bg-slate-200 text-slate-600';
      default:
        return 'bg-rose-100 text-rose-700';
    }
  }

  private refreshNextNumber(): void {
    this.http
      .nextNumber()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ number }) => this.form.controls.number.setValue(number),
        error: () => undefined,
      });
  }

  private resetForm(branchId: string): void {
    this.form.controls.patientInitials.setValue('');
    this.form.controls.items.clear();
    this.form.controls.items.push(this.fb.nonNullable.control('', [Validators.required]));
    this.form.controls.branchId.setValue(branchId);
+
+    // After clearing the form we want it to appear pristine again – otherwise the required
+    // validation messages (e.g. "Patient initials are required…") show immediately after a
+    // successful submit. Mark the form untouched so errors are only shown after the user interacts.
+    this.form.markAsUntouched();
 
     // The number we just used is now taken: clear it before the suggestion arrives so an
     // immediate second submit cannot resend it and get "already exists".
     this.form.controls.number.setValue('');
     this.refreshNextNumber();
   }
}
