// ============================================================
// ION CRM — TypeScript Type Definitions
// ============================================================

// ----- Enums -----

export type UserRole = 'ProjectAdmin' | 'SalesManager' | 'SalesRep' | 'Accounting';

export type CustomerStatus = 'Lead' | 'Active' | 'Inactive' | 'Churned';

export type CustomerSegment = 'SME' | 'Enterprise' | 'Startup' | 'Government' | 'Individual';

export type CustomerLabel =
  | 'YuksekPotansiyel'
  | 'Potansiyel'
  | 'Notr'
  | 'Vasat'
  | 'Kotu';

export type ContactType = 'Call' | 'Email' | 'Meeting' | 'Note' | 'WhatsApp' | 'Visit';

export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export type TaskStatus = 'Todo' | 'InProgress' | 'Done' | 'Cancelled';

export type OpportunityStage =
  | 'Prospecting'
  | 'Qualification'
  | 'Proposal'
  | 'Negotiation'
  | 'ClosedWon'
  | 'ClosedLost';

export type SyncSource = 'SaasA' | 'SaasB';
export type SyncDirection = 'Inbound' | 'Outbound';
export type SyncStatus = 'Pending' | 'Success' | 'Failed' | 'Retrying';

// ----- API Response Wrapper -----

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  message: string | null;
  errors: string[] | null;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  /** Backend returns `page` (not `pageNumber`) — matches ASP.NET Core PagedResult */
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

// ----- Auth -----

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface LoginResponse {
  accessToken: string;
  expiresIn: number;
  user: AuthUser;
}

export interface AuthUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  isSuperAdmin: boolean;
  projectRoles: Record<string, UserRole>;
}

// ----- Project / Tenant -----

export interface Project {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// ----- User -----

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  isSuperAdmin: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  projectRoles: UserProjectRole[];
}

export interface UserProjectRole {
  projectId: string;
  projectName: string;
  role: UserRole;
}

export interface CreateUserRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  isSuperAdmin: boolean;
}

export interface UpdateUserRequest {
  firstName: string;
  lastName: string;
  isActive: boolean;
}

// ----- Customer -----

export interface Customer {
  id: string;
  projectId: string;
  code: string | null;
  companyName: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  taxNumber: string | null;
  taxUnit: string | null;
  status: CustomerStatus;
  segment: CustomerSegment | null;
  label: CustomerLabel | null;
  assignedUserId: string | null;
  assignedUserName: string | null;
  legacyId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCustomerRequest {
  /** Required by backend — must match one of the user's project IDs */
  projectId: string;
  companyName: string;
  contactName?: string;
  email?: string;
  phone?: string;
  address?: string;
  taxNumber?: string;
  taxUnit?: string;
  status: CustomerStatus;
  segment?: CustomerSegment;
  label?: CustomerLabel;
  assignedUserId?: string;
  code?: string;
}

export interface UpdateCustomerRequest extends CreateCustomerRequest {
  id: string;
}

export interface CustomerListParams {
  /** 1-based page number — matches backend `page` query param */
  page?: number;
  pageSize?: number;
  search?: string;
  status?: CustomerStatus;
  segment?: CustomerSegment;
  label?: CustomerLabel;
  assignedUserId?: string;
}

// ----- Contact History -----

export interface ContactHistory {
  id: string;
  customerId: string;
  customerName: string | null;
  projectId: string;
  type: ContactType;
  subject: string | null;
  content: string | null;
  outcome: string | null;
  contactedAt: string;
  createdByUserId: string | null;
  createdByUserName: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateContactHistoryRequest {
  customerId: string;
  type: ContactType;
  subject?: string;
  content?: string;
  outcome?: string;
  contactedAt: string;
}

// ----- Task -----

export interface CustomerTask {
  id: string;
  customerId: string;
  customerName: string;
  projectId: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  priority: TaskPriority;
  status: TaskStatus;
  assignedUserId: string | null;
  assignedUserName: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateTaskRequest {
  customerId: string;
  title: string;
  description?: string;
  dueDate?: string;
  priority: TaskPriority;
  assignedUserId?: string;
}

export interface UpdateTaskRequest extends CreateTaskRequest {
  id: string;
  status: TaskStatus;
}

// ----- Opportunity -----

export interface Opportunity {
  id: string;
  customerId: string;
  customerName: string;
  projectId: string;
  title: string;
  value: number | null;
  stage: OpportunityStage;
  probability: number | null;
  expectedCloseDate: string | null;
  assignedUserId: string | null;
  assignedUserName: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOpportunityRequest {
  customerId: string;
  title: string;
  value?: number;
  stage: OpportunityStage;
  probability?: number;
  expectedCloseDate?: string;
  assignedUserId?: string;
}

export interface UpdateOpportunityRequest extends CreateOpportunityRequest {
  id: string;
}

// ----- Sync Log -----

export interface SyncLog {
  id: string;
  projectId: string;
  source: SyncSource;
  direction: SyncDirection;
  entityType: string;
  entityId: string | null;
  status: SyncStatus;
  errorMessage: string | null;
  retryCount: number;
  syncedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

// ----- Dashboard -----

export interface DashboardStats {
  totalCustomers: number;
  activeCustomers: number;
  newLeadsThisMonth: number;
  openTasks: number;
  openOpportunities: number;
  pipelineValue: number;
  monthlyActivity: MonthlyActivity[];
  customersByStatus: StatusBreakdown[];
  opportunitiesByStage: StageBreakdown[];
  recentActivities: RecentActivity[];
}

export interface MonthlyActivity {
  month: string;
  calls: number;
  meetings: number;
  emails: number;
}

export interface StatusBreakdown {
  status: CustomerStatus;
  count: number;
}

export interface StageBreakdown {
  stage: OpportunityStage;
  count: number;
  value: number;
}

export interface RecentActivity {
  id: string;
  type: ContactType;
  customerName: string;
  subject: string | null;
  createdByUserName: string | null;
  contactedAt: string;
}
