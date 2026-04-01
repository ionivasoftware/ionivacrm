// ============================================================
// ION CRM — TypeScript Type Definitions
// ============================================================

// ----- Enums -----

export type UserRole = 'ProjectAdmin' | 'SalesManager' | 'SalesRep' | 'Accounting';

export type CustomerStatus = 'Lead' | 'Active' | 'Demo' | 'Churned' | 'Passive';

/** Segment is a free string — project-specific values defined in projectSegments config */
export type CustomerSegment = string;

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
  | 'YeniArama'
  | 'Potansiyel'
  | 'Demo'
  | 'Musteri'
  | 'Kayip';

// ----- Invoice -----

export type InvoiceStatus = 'Draft' | 'TransferredToParasut' | 'Paid' | 'Cancelled';

export interface Invoice {
  id: string;
  projectId: string;
  customerId: string;
  customerName: string;
  title: string;
  description: string | null;
  invoiceSeries: string | null;
  invoiceNumber: number | null;
  issueDate: string;
  dueDate: string;
  currency: string;
  grossTotal: number;
  netTotal: number;
  linesJson: string;
  status: InvoiceStatus;
  parasutId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface InvoiceLineItem {
  description: string;
  quantity: number;
  unitPrice: number;
  vatRate: number;
  discountValue: number;
  discountType: string;
  unit: string;
}

export interface CreateCrmInvoiceRequest {
  customerId: string;
  title: string;
  description?: string;
  invoiceSeries?: string;
  invoiceNumber?: number;
  issueDate: string;
  dueDate: string;
  currency?: string;
  grossTotal: number;
  netTotal: number;
  linesJson: string;
}

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
  expirationDate: string | null;
  legacyId: string | null;
  parasutContactId: string | null;
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

// ----- Paraşüt Product Mapping -----

/** Fixed product key values used for CRM product mapping */
export type ParasutProductKey =
  | 'membership_monthly'
  | 'membership_yearly'
  | 'sms_1000'
  | 'sms_2500'
  | 'sms_5000'
  | 'sms_10000';

/** A saved mapping between a CRM product key and a Paraşüt product */
export interface ParasutProduct {
  id: string;
  projectId: string;
  productKey: ParasutProductKey;
  productName: string;
  parasutProductId: string;
  parasutProductName: string | null;
  unitPrice: number;
  /** Tax rate as decimal: 0.20 = 20% */
  taxRate: number;
  createdAt: string;
  updatedAt: string;
}

/** A live product item fetched from the Paraşüt API */
export interface ParasutProductListItem {
  id: string;
  name: string;
  unitPrice: number;
  /** Tax rate as decimal: 0.20 = 20% */
  vatRate: number;
  currency: string;
  unit: string | null;
  archived: boolean;
}

// ----- Dashboard -----

export interface ExpiringCustomer {
  id: string;
  companyName: string;
  contactName: string | null;
  phone: string | null;
  expirationDate: string;
  daysLeft: number;
}

export interface DashboardStats {
  totalCustomers: number;
  activeCustomers: number;
  newLeadsThisMonth: number;
  openTasks: number;
  monthlyActivity: MonthlyActivity[];
  customersByStatus: StatusBreakdown[];
  opportunitiesByStage: StageBreakdown[];
  recentActivities: RecentActivity[];
  expiringCustomers: ExpiringCustomer[];
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
}

export interface RecentActivity {
  id: string;
  type: ContactType;
  customerName: string;
  subject: string | null;
  createdByUserName: string | null;
  contactedAt: string;
}
