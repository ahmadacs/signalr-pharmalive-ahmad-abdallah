export const ROLES = ['Doctor', 'Pharmacist', 'Display'] as const;
export type Role = (typeof ROLES)[number];

export const STATUSES = [
  'New',
  'Claimed',
  'Preparing',
  'Ready',
  'Dispensed',
  'Rejected',
] as const;

export type PrescriptionStatus = (typeof STATUSES)[number];

/** Statuses that can be reached from each status, used to render the right buttons. */
export const NEXT_STATUSES: Partial<Record<PrescriptionStatus, PrescriptionStatus[]>> = {
  New: ['Claimed', 'Rejected'],
  Claimed: ['Preparing', 'Rejected'],
  Preparing: ['Ready', 'Rejected'],
  Ready: ['Dispensed', 'Rejected'],
  Dispensed: [],
  Rejected: [],
};

export interface Branch {
  id: string;
  name: string;
}

export interface Prescription {
  id: string;
  number: string;
  branchId: string;
  patientInitials: string;
  items: string[];
  createdBy: string;
  status: PrescriptionStatus;
  claimedBy: string | null;
  createdAt: string;
  claimedAt: string | null;
  preparingAt: string | null;
  readyAt: string | null;
  dispensedAt: string | null;
  rejectedAt: string | null;
}

export interface BranchPresence {
  branchId: string;
  pharmacists: string[];
}

export interface BranchStats {
  branchId: string;
  new: number;
  claimed: number;
  preparing: number;
  ready: number;
  dispensed: number;
  rejected: number;
  total: number;
  averagePreparationSeconds: number | null;
  generatedAt: string;
}

export interface TokenResponse {
  accessToken: string;
  userName: string;
  role: Role;
  expiresAt: string;
}

export interface ValidationProblem {
  type?: string;
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
