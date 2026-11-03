import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from './config';
import { Branch, Prescription } from './models';

export interface CreatePrescriptionRequest {
  number: string;
  branchId: string;
  patientInitials: string;
  items: string[];
}

/** Plain HTTP calls: used only for the initial page load and for creating a prescription. */
@Injectable({ providedIn: 'root' })
export class PrescriptionsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = inject(API_URL);

  branches(): Observable<Branch[]> {
    return this.http.get<Branch[]>(`${this.apiUrl}/api/branches`);
  }

  queue(branchId: string): Observable<Prescription[]> {
    return this.http.get<Prescription[]>(`${this.apiUrl}/api/prescriptions`, {
      params: { branchId },
    });
  }

  mine(): Observable<Prescription[]> {
    return this.http.get<Prescription[]>(`${this.apiUrl}/api/prescriptions/mine`);
  }

  nextNumber(): Observable<{ number: string }> {
    return this.http.get<{ number: string }>(`${this.apiUrl}/api/prescriptions/next-number`);
  }

  create(request: CreatePrescriptionRequest): Observable<Prescription> {
    return this.http.post<Prescription>(`${this.apiUrl}/api/prescriptions`, request);
  }
}
