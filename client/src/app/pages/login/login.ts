import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService, homeFor } from '../../core/auth.service';
import { ROLES, Role, ValidationProblem } from '../../core/models';

interface DemoUser {
  userName: string;
  role: Role;
  hint: string;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly roles = ROLES;
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly serverErrors = signal<Record<string, string[]>>({});

  protected readonly demoUsers: DemoUser[] = [
    { userName: 'dr.khaled', role: 'Doctor', hint: 'creates prescriptions' },
    { userName: 'sara', role: 'Pharmacist', hint: 'RAM-01 queue' },
    { userName: 'omar', role: 'Pharmacist', hint: 'RAM-01 queue' },
    { userName: 'display', role: 'Display', hint: 'waiting-room screen' },
  ];

  protected readonly form = inject(FormBuilder).nonNullable.group({
    userName: ['', [Validators.required, Validators.maxLength(30)]],
    role: ['Pharmacist' as Role, [Validators.required]],
  });

  protected fieldError(field: 'userName' | 'role'): string | null {
    const server = this.serverErrors()[field]?.[0];
    if (server) return server;

    const control = this.form.controls[field];
    if (!control.touched || control.valid) return null;

    if (control.hasError('required')) {
      return field === 'userName' ? 'User name is required.' : 'Choose a role.';
    }

    return 'Use 30 characters or fewer.';
  }

  protected useDemoUser(user: DemoUser): void {
    this.form.setValue({ userName: user.userName, role: user.role });
    this.submit();
  }

  protected submit(): void {
    if (this.submitting()) return;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    this.serverErrors.set({});

    const { userName, role } = this.form.getRawValue();

    this.auth
      .login(userName.trim(), role)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          this.submitting.set(false);
          this.router.navigateByUrl(homeFor(session.role));
        },
        error: (error: unknown) => {
          this.submitting.set(false);

          const body = (error as { error?: ValidationProblem })?.error;
          if (body?.errors) {
            this.serverErrors.set(body.errors);
            return;
          }

          const status = (error as { status?: number })?.status;
          this.error.set(
            status === 0
              ? 'Cannot reach the API on http://localhost:5000. Start the server and try again.'
              : `Login failed (HTTP ${status ?? 'unknown'}). Please try again.`,
          );
        },
      });
  }
}
