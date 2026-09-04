import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type {
  ApiResponse,
  PaginatedResponse,
  Customer,
  CreateCustomerRequest,
  UpdateCustomerRequest,
  CustomerListParams,
  ContactHistory,
  CreateContactHistoryRequest,
  CustomerTask,
  CreateTaskRequest,
  UpdateTaskRequest,
  TaskStatus,
  Opportunity,
  CreateOpportunityRequest,
  UpdateOpportunityRequest,
  OpportunityStage,
  EmsUser,
  EmsSummary,
  RezervalSummary,
  RezervalReservationSetting,
  UpdateRezervalReservationSettingRequest,
} from '@/types';

// ── Customer CRUD ─────────────────────────────────────────────────────────────

export function useCustomers(params: CustomerListParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['customers', projectId, params],
    queryFn: async () => {
      // Backend uses JWT claims to filter by project (not a query param).
      // Map `page` → backend expects `page` (not `pageNumber`).
      // Exclude `undefined` values so they don't show as empty query params.
      const queryParams: Record<string, string | number | undefined> = {};
      if (params.search) queryParams.search = params.search;
      if (params.status) queryParams.status = params.status;
      if (params.segment) queryParams.segment = params.segment;
      if (params.assignedUserId) queryParams.assignedUserId = params.assignedUserId;
      if (params.label) queryParams.label = params.label;
      if (params.sortBy) queryParams.sortBy = params.sortBy;
      queryParams.page = params.page ?? 1;
      queryParams.pageSize = params.pageSize ?? 20;
      if (projectId) queryParams.projectId = projectId;

      const response = await apiClient.get<ApiResponse<PaginatedResponse<Customer>>>(
        '/customers',
        { params: queryParams }
      );
      return response.data.data;
    },
    enabled: true, // SuperAdmin can see all customers without projectId
  });
}

export function useCustomer(id: string) {
  return useQuery({
    queryKey: ['customer', id],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<Customer>>(`/customers/${id}`);
      return response.data.data;
    },
    enabled: !!id,
  });
}

export function useCreateCustomer() {
  const queryClient = useQueryClient();
  // Resolve projectId from store — backend requires it in the request body
  const projectId = useAuthStore((s) => s.currentProjectId);

  return useMutation({
    mutationFn: async (data: Omit<CreateCustomerRequest, 'projectId'>) => {
      if (!projectId) throw new Error('Proje seçili değil. Lütfen bir proje seçiniz.');
      const payload: CreateCustomerRequest = { ...data, projectId };
      const response = await apiClient.post<ApiResponse<Customer>>('/customers', payload);
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateCustomerRequest) => {
      const response = await apiClient.put<ApiResponse<Customer>>(
        `/customers/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['customer', variables.id] });
    },
  });
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/customers/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── All Contact Histories (global) ────────────────────────────────────────────

export interface AllContactHistoriesParams {
  projectId?: string;
  customerId?: string;
  type?: string;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export function useAllContactHistories(params: AllContactHistoriesParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allContactHistories', params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<ContactHistory>>>(
        '/contact-histories',
        { params: { projectId, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 60 * 1000,
  });
}

// ── Contact History (per customer) ────────────────────────────────────────────

export function useContactHistory(customerId: string) {
  return useQuery({
    queryKey: ['contactHistory', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<ContactHistory>>>(
        `/customers/${customerId}/contact-histories`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateContactHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateContactHistoryRequest) => {
      const response = await apiClient.post<ApiResponse<ContactHistory>>(
        `/customers/${data.customerId}/contact-histories`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['contactHistory', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Tasks ─────────────────────────────────────────────────────────────────────

export function useCustomerTasks(customerId: string) {
  return useQuery({
    queryKey: ['customerTasks', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<CustomerTask>>>(
        `/customers/${customerId}/tasks`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateTaskRequest) => {
      const response = await apiClient.post<ApiResponse<CustomerTask>>(
        `/customers/${data.customerId}/tasks`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateTaskRequest) => {
      const response = await apiClient.put<ApiResponse<CustomerTask>>(
        `/customers/${data.customerId}/tasks/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
    },
  });
}

// ── Opportunities ─────────────────────────────────────────────────────────────

export function useCustomerOpportunities(customerId: string) {
  return useQuery({
    queryKey: ['customerOpportunities', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<Opportunity>>>(
        `/customers/${customerId}/opportunities`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateOpportunityRequest) => {
      const response = await apiClient.post<ApiResponse<Opportunity>>(
        `/customers/${data.customerId}/opportunities`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerOpportunities', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateOpportunityRequest) => {
      const response = await apiClient.put<ApiResponse<Opportunity>>(
        `/customers/${data.customerId}/opportunities/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerOpportunities', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['allOpportunities'] });
    },
  });
}

// ── Project-level Tasks ────────────────────────────────────────────────────────

export interface AllTasksParams {
  status?: TaskStatus;
  priority?: string;
  page?: number;
  pageSize?: number;
}

export function useAllTasks(params: AllTasksParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allTasks', projectId, params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<CustomerTask>>>(
        '/tasks',
        { params: { projectId, pageSize: 200, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 30 * 1000,
  });
}

export function useUpdateTaskStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ customerId, taskId, status }: { customerId: string; taskId: string; status: TaskStatus }) => {
      const response = await apiClient.patch<ApiResponse<CustomerTask>>(
        `/customers/${customerId}/tasks/${taskId}/status`,
        { status }
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['allTasks'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Project-level Opportunities (Pipeline) ────────────────────────────────────

export interface AllOpportunitiesParams {
  stage?: OpportunityStage;
  page?: number;
  pageSize?: number;
}

export function useAllOpportunities(params: AllOpportunitiesParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allOpportunities', projectId, params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<Opportunity>>>(
        '/pipeline',
        { params: { projectId, pageSize: 200, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 30 * 1000,
  });
}

export function useUpdateOpportunityStage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, stage }: { id: string; stage: OpportunityStage }) => {
      const response = await apiClient.patch<ApiResponse<Opportunity>>(
        `/pipeline/${id}/stage`,
        { stage }
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['allOpportunities'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Transfer Lead ─────────────────────────────────────────────────────────────

export function useTransferLeadCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ leadId, targetCustomerId }: { leadId: string; targetCustomerId: string }) => {
      await apiClient.post(`/customers/${leadId}/transfer/${targetCustomerId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Add Customer SMS ──────────────────────────────────────────────────────────

export interface AddCustomerSmsResult {
  companyId: number;
  smsCount: number;
  added: number;
  invoiceCreated: boolean;
  invoiceId: string | null;
}

export function useAddCustomerSms(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { count: number }) => {
      const response = await apiClient.post<ApiResponse<AddCustomerSmsResult>>(
        `/customers/${customerId}/add-sms`,
        body
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
    },
  });
}

// ── EMS Users ─────────────────────────────────────────────────────────────────

export interface SetPrimaryEmsUserResult {
  companyId: number;
  userId: string;
  previousUserId: string | null;
}

/**
 * Changes the firm's primary admin to one of its existing users. SuperAdmin only; the endpoint
 * requires `?confirm=true`. Invalidates the EMS users list so the new primary badge reflects.
 */
export function useSetPrimaryEmsUser(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { userId: string }) => {
      const response = await apiClient.post<ApiResponse<SetPrimaryEmsUserResult>>(
        `/customers/${customerId}/set-primary-admin?confirm=true`,
        body
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customerEmsUsers', customerId] });
    },
  });
}

export function useCustomerEmsUsers(customerId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['customerEmsUsers', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<EmsUser[]>>(
        `/customers/${customerId}/ems-users`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
  });
}

// ── EMS Company Summary ───────────────────────────────────────────────────────

export function useCustomerEmsSummary(customerId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['customerEmsSummary', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<EmsSummary>>(
        `/customers/${customerId}/ems-summary`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
    staleTime: 5 * 60 * 1000,
  });
}

// ── Rezerval Company Summary ──────────────────────────────────────────────────

export function useCustomerRezervalSummary(customerId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['customerRezervalSummary', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<RezervalSummary>>(
        `/customers/${customerId}/rezerval-summary`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
    staleTime: 5 * 60 * 1000,
  });
}

// ── Rezerval Reservation Settings ────────────────────────────────────────────

export function useCustomerRezervalSettings(customerId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['customerRezervalSettings', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<RezervalReservationSetting>>(
        `/customers/${customerId}/rezerval-settings`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
    staleTime: 60 * 1000,
  });
}

export function useUpdateCustomerRezervalSettings(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateRezervalReservationSettingRequest) => {
      const response = await apiClient.put<ApiResponse<string>>(
        `/customers/${customerId}/rezerval-settings`,
        data
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customerRezervalSettings', customerId] });
    },
  });
}

// ── Push to RezervAl ─────────────────────────────────────────────────────────

export interface PushToRezervalRequest {
  name: string;
  title: string;
  phone: string;
  email: string;
  taxUnit: string;
  taxNumber: string;
  tcNo?: string;
  isPersonCompany: boolean;
  address: string;
  language?: number;
  countryPhoneCode?: number;
  experationDate?: string;
  adminNameSurname?: string;
  adminLoginName?: string;
  adminPassword?: string;
  adminEmail?: string;
  adminPhone?: string;
  /** Logo file encoded as Base64 string (optional) */
  logoBase64?: string;
  /** Logo file name e.g. "logo.png" (optional) */
  logoFileName?: string;
}

export function usePushToRezerval(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: PushToRezervalRequest) => {
      const response = await apiClient.post<ApiResponse<Customer>>(
        `/customers/${customerId}/push-to-rezerval`,
        body
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
      queryClient.invalidateQueries({ queryKey: ['customers'] });
    },
  });
}

// ── EMS Extend Expiration ─────────────────────────────────────────────────────

export interface ExtendExpirationResult {
  newExpirationDate: string;
  invoiceCreated: boolean;
  invoiceId: string | null;
  invoiceError: string | null;
}

export function useExtendEmsExpiration(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: {
      durationType: string;
      amount: number;
      /** İskonto — gönderilmezse iskontosuz faturalanır. */
      discountValue?: number;
      /** 'percentage' (varsayılan) | 'amount'. */
      discountType?: 'percentage' | 'amount';
    }) => {
      const response = await apiClient.post<ApiResponse<ExtendExpirationResult>>(
        `/customers/${customerId}/extend-expiration`,
        body
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}

// ── Customer Contracts (Rezerval recurring subscription) ─────────────────────

/** String values match the C# enum serialised via JsonStringEnumConverter. */
export type ContractPaymentType = 'CreditCard' | 'EftWire';
/** String values match the C# enum serialised via JsonStringEnumConverter. */
export type ContractStatus = 'Active' | 'Completed' | 'Cancelled';

export interface CustomerContract {
  id: string;
  customerId: string;
  title: string;
  monthlyAmount: number;
  paymentType: ContractPaymentType;
  startDate: string;
  durationMonths: number | null;
  endDate: string | null;
  status: ContractStatus;
  rezervalSubscriptionId: string | null;
  rezervalPaymentPlanId: string | null;
  nextInvoiceDate: string | null;
  lastInvoiceGeneratedDate: string | null;
  createdAt: string;
}

export interface CreateContractRequest {
  monthlyAmount: number;
  paymentType: ContractPaymentType;
  /** ISO date "yyyy-MM-dd" — backend converts to UTC midnight. */
  startDate: string;
  durationMonths: number | null;
}

/**
 * Returns the currently active contract for a customer, or null when none exists.
 * Caller should pass `enabled` only when the customer is a Rezerval customer.
 */
export function useActiveCustomerContract(customerId: string, enabled: boolean = true) {
  return useQuery({
    queryKey: ['customer-contract', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<CustomerContract | null>>(
        `/customers/${customerId}/contracts/active`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
  });
}

/**
 * Creates (or renews) a customer contract. Renewal semantics: any existing
 * active contract is automatically marked Completed before the new one is created.
 */
export function useCreateCustomerContract(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateContractRequest) => {
      const response = await apiClient.post<ApiResponse<CustomerContract>>(
        `/customers/${customerId}/contracts`,
        body
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer-contract', customerId] });
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}

/** Changes the payment type of the active contract. */
export function useUpdateContractPaymentType(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (paymentType: ContractPaymentType) => {
      const response = await apiClient.patch<ApiResponse<CustomerContract>>(
        `/customers/${customerId}/contracts/payment-type`,
        { paymentType }
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer-contract', customerId] });
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
    },
  });
}

/**
 * Response from POST /customers/{id}/contracts/cancel.
 * `iyzicoWarnings` is non-empty when Rezerval reported iyzico-side issues
 * (already deleted plan/product, network timeout) — local cleanup still ran.
 */
export interface CancelContractResponse {
  contract: CustomerContract;
  iyzicoWarnings: string[];
}

/**
 * Cancels the active recurring contract for a Rezerval customer.
 * Tolerant: even when iyzico warnings are returned, the local contract is marked Cancelled.
 */
export function useCancelCustomerContract(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const response = await apiClient.post<ApiResponse<CancelContractResponse>>(
        `/customers/${customerId}/contracts/cancel`
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customer-contract', customerId] });
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}

// ── Liftdesk Checklists ───────────────────────────────────────────────────────

export type ChecklistKind = 'maintenance' | 'fault';

/** Equipment family of a MAINTENANCE checklist (Liftdesk ChecklistType). Fault lists are not split. */
export const CHECKLIST_TYPE = { Elevator: 1, Escalator: 2 } as const;
export type ChecklistType = (typeof CHECKLIST_TYPE)[keyof typeof CHECKLIST_TYPE];

/**
 * The three editable lists, flattened for the UI. Each maps to a (kind, type) pair — and to the
 * reset scope of the same name.
 */
export type ChecklistListKey = 'maintenance' | 'escalator' | 'fault';

/** Checklist languages Liftdesk knows by name; anything else is shown as its raw number. */
export const CHECKLIST_CULTURE_LABEL: Record<number, string> = { 1: 'Türkçe', 2: 'İngilizce' };
export const cultureLabel = (c: number) => CHECKLIST_CULTURE_LABEL[c] ?? `Dil ${c}`;

export const CHECKLIST_LISTS: Record<
  ChecklistListKey,
  { label: string; kind: ChecklistKind; type?: ChecklistType }
> = {
  maintenance: { label: 'Bakım — Asansör', kind: 'maintenance', type: CHECKLIST_TYPE.Elevator },
  escalator:   { label: 'Bakım — Yürüyen Merdiven', kind: 'maintenance', type: CHECKLIST_TYPE.Escalator },
  fault:       { label: 'Arıza', kind: 'fault' },
};

export interface ChecklistItem {
  id: string;
  text: string;
  sortOrder: number;
  isActive: boolean;
}

export interface ChecklistHeader {
  id: string;
  title: string;
  sortOrder: number;
  isActive: boolean;
  items: ChecklistItem[];
}

/** A company's full checklist document for one kind. Includes inactive (not deleted) rows. */
export interface ChecklistDoc {
  companyId: number;
  kind: string;
  formId: number;
  headers: ChecklistHeader[];
  /** 1 = elevator, 2 = escalator. Always 1 on the fault list. */
  type: number;
  /** Language of the returned rows (1 = TR, 2 = EN…). */
  culture: number;
  /** Languages that have rows for this list — fills the language picker. Null/1 item → no picker. */
  availableCultures: number[] | null;
}

export interface ChecklistResetResult {
  companyId: number;
  maintenance: ChecklistDoc | null;
  fault: ChecklistDoc | null;
  /** Escalator maintenance list — filled when the reset scope was 'escalator' or 'both'. */
  escalatorMaintenance: ChecklistDoc | null;
}

export interface ChecklistItemInput {
  text: string;
  isActive: boolean;
}

export interface ChecklistHeaderInput {
  title: string;
  isActive: boolean;
  items: ChecklistItemInput[];
}

/** Builds the ?type=&culture= scope shared by the read and the replace. */
function checklistScope(list: ChecklistListKey, culture?: number): string {
  const { type } = CHECKLIST_LISTS[list];
  const parts: string[] = [];
  if (type) parts.push(`type=${type}`);
  if (culture) parts.push(`culture=${culture}`);
  return parts.length ? `?${parts.join('&')}` : '';
}

export function useCustomerChecklist(
  customerId: string,
  list: ChecklistListKey,
  culture: number | undefined,
  enabled: boolean
) {
  const { kind } = CHECKLIST_LISTS[list];
  return useQuery({
    // Keyed by list AND culture: each (equipment family, language) pair is its own document.
    queryKey: ['customerChecklist', customerId, list, culture ?? 'default'],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<ChecklistDoc>>(
        `/customers/${customerId}/checklists/${kind}${checklistScope(list, culture)}`
      );
      return response.data.data;
    },
    enabled: !!customerId && enabled,
  });
}

/**
 * Full-document replace: the sent headers become the NEW checklist (order = sort order).
 * The replace is scoped to the list's `type`, so it must be saved with the same list it was read
 * with — otherwise the other equipment family would be overwritten. An empty set is rejected;
 * emptying is only possible through reset.
 */
export function useUpdateCustomerChecklist(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (
      { list, culture, headers }:
      { list: ChecklistListKey; culture?: number; headers: ChecklistHeaderInput[] }
    ) => {
      const { kind } = CHECKLIST_LISTS[list];
      const response = await apiClient.put<ApiResponse<ChecklistDoc>>(
        `/customers/${customerId}/checklists/${kind}${checklistScope(list, culture)}`,
        { headers }
      );
      return response.data.data;
    },
    onSuccess: (data, { list, culture }) => {
      if (data) queryClient.setQueryData(['customerChecklist', customerId, list, culture ?? 'default'], data);
    },
  });
}

/** DESTRUCTIVE: re-seeds the Liftdesk default template, deleting the company's customisation. */
export function useResetCustomerChecklists(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ kind, culture }: { kind: ChecklistListKey | 'both'; culture?: number }) => {
      const response = await apiClient.post<ApiResponse<ChecklistResetResult>>(
        `/customers/${customerId}/checklists/reset`,
        { kind, culture }
      );
      return response.data.data;
    },
    // Invalidate rather than seed: cache entries are keyed by (list, culture) and the reset response
    // carries no key for the entries it did not touch, so precise seeding would go stale silently.
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customerChecklist', customerId] });
    },
  });
}

// ── Liftdesk Subscription Plan ────────────────────────────────────────────────

export interface CompanyCurrentPlan {
  planId: string;
  name: string;
  tier: string;
  /** Trialing | Active | PendingPayment | Cancelled | Expired */
  status: string;
  billingPeriod: string | null;
  startDate: string | null;
  endDate: string | null;
  autoRenew: boolean;
}

export interface CompanyAvailablePlan {
  planId: string;
  name: string;
  tier: string;
  priceMonthly: number;
  priceYearly: number;
}

export interface CompanyPlan {
  companyId: number;
  /** Null for legacy tenants with no subscription row — a change would 409. */
  current: CompanyCurrentPlan | null;
  availablePlans: CompanyAvailablePlan[];
  /** Operator warning (typically: iyzico keeps charging the old amount). Null when there is none. */
  warning: string | null;
}

export function useCustomerPlan(customerId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['customerPlan', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<CompanyPlan>>(`/customers/${customerId}/plan`);
      return response.data.data;
    },
    enabled: !!customerId && enabled,
  });
}

export interface UpdateCustomerPlanInput {
  /** Send planId (preferred — unambiguous) or tier; planId wins on the Liftdesk side. */
  planId?: string;
  tier?: string;
  /** Omit to keep the current billing period. */
  billingPeriod?: 'Monthly' | 'Yearly';
}

/** Changes the tier (SuperAdmin only). Takes effect immediately; does NOT extend the licence period. */
export function useUpdateCustomerPlan(customerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateCustomerPlanInput) => {
      const response = await apiClient.put<ApiResponse<CompanyPlan>>(
        `/customers/${customerId}/plan`,
        body
      );
      return response.data.data;
    },
    onSuccess: (data) => {
      // The PUT returns the same payload as the GET — seed it instead of refetching.
      if (data) queryClient.setQueryData(['customerPlan', customerId], data);
      queryClient.invalidateQueries({ queryKey: ['customer', customerId] });
    },
  });
}
