import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, queryString } from '../lib/api';
import type {
  AdminProduct, Customer, Dashboard, Inventory, OrderDetail, OrderList,
  PagedAdmin, ShopSettings, StitchingBoard,
} from '../types/admin';

/**
 * Shop-panel data.
 *
 * Nothing here is cached for long. The panel is what Saad watches while orders
 * come in, and a stale stock count or order status on this screen is worse than
 * a little more load on a shop doing tens of orders a day.
 */

const MINUTE = 60 * 1000;

export function useDashboard(asAt?: string) {
  return useQuery({
    queryKey: ['admin', 'dashboard', asAt ?? 'today'],
    queryFn: ({ signal }) => api.get<Dashboard>(`/admin/dashboard${queryString({ asAt })}`, signal),
    staleTime: MINUTE,
    refetchInterval: 2 * MINUTE,
  });
}

export interface OrderSearch {
  status?: string; search?: string;
  fromDate?: string; toDate?: string;
  page?: number; pageSize?: number;
}

export function useOrders(query: OrderSearch) {
  return useQuery({
    queryKey: ['admin', 'orders', query],
    queryFn: ({ signal }) => api.get<OrderList>(`/admin/orders${queryString({ ...query })}`, signal),
    staleTime: 30_000,
    placeholderData: previous => previous,
  });
}

export function useOrder(orderId: number | undefined) {
  return useQuery({
    queryKey: ['admin', 'order', orderId],
    queryFn: ({ signal }) => api.get<OrderDetail>(`/admin/orders/${orderId}`, signal),
    enabled: Boolean(orderId),
  });
}

export function useUpdateOrderStatus(orderId: number) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body: { status: string; note?: string | null }) =>
      api.patch<void>(`/admin/orders/${orderId}/status`, body),
    onSuccess: () => {
      // A status move can return stock to the shelf and changes what the
      // overview and the floor show, so the whole panel is refetched rather
      // than surgically patched — cheap at this size, and never wrong.
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useSaveMeasurements(orderId: number) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body: Record<string, unknown>) =>
      api.post<void>(`/admin/orders/${orderId}/measurements`, body),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['admin'] }),
  });
}

export function useInventory(query: { search?: string; lowStockOnly?: boolean }) {
  return useQuery({
    queryKey: ['admin', 'inventory', query],
    queryFn: ({ signal }) => api.get<Inventory>(`/admin/inventory${queryString({ ...query })}`, signal),
    staleTime: 30_000,
  });
}

export function useAdjustStock(productId: number) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body: { delta: number; reason: string }) =>
      api.post<{ productId: number; stock: number }>(`/admin/inventory/${productId}/adjust`, body),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useStitchingBoard() {
  return useQuery({
    queryKey: ['admin', 'stitching'],
    queryFn: ({ signal }) => api.get<StitchingBoard>('/admin/stitching-queue', signal),
    staleTime: 30_000,
    refetchInterval: 2 * MINUTE,
  });
}

export function useUpdateStitchingJob() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ jobId, ...body }: { jobId: number; stage?: string; assignedTo?: string | null }) =>
      api.patch<void>(`/admin/stitching-queue/${jobId}`, body),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['admin'] }),
  });
}

export interface CustomerSearch {
  search?: string; fromDate?: string; toDate?: string;
  page?: number; pageSize?: number;
}

export function useCustomers(query: CustomerSearch) {
  return useQuery({
    queryKey: ['admin', 'customers', query],
    queryFn: ({ signal }) =>
      api.get<PagedAdmin<Customer>>(`/admin/customers${queryString({ ...query })}`, signal),
    staleTime: MINUTE,
    placeholderData: previous => previous,
  });
}

export function useAdminProducts(
  query: { search?: string; page?: number; pageSize?: number; archivedOnly?: boolean },
) {
  return useQuery({
    queryKey: ['admin', 'products', query],
    queryFn: ({ signal }) =>
      api.get<PagedAdmin<AdminProduct>>(`/admin/products${queryString({ ...query })}`, signal),
    staleTime: 30_000,
    placeholderData: previous => previous,
  });
}

/**
 * Create or update, depending on whether an id was given.
 *
 * Create returns the new id and update returns 204, so the result type is the
 * union — a caller that needs the id after a create must narrow it.
 */
export function useSaveProduct(productId?: number) {
  const client = useQueryClient();

  return useMutation<{ productId: number } | void, Error, Record<string, unknown>>({
    mutationFn: async body =>
      productId
        ? api.put<void>(`/admin/products/${productId}`, body)
        : api.post<{ productId: number }>('/admin/products', body),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

/**
 * Archiving and un-archiving a product.
 *
 * Deliberately not called "delete": the row is never removed, because the
 * order lines that name the product are the shop's own sales history. Both
 * invalidate the storefront's cache as well as the panel's — an archived
 * product has to leave the public catalogue, and a restored one has to
 * reappear in it.
 */
export function useArchiveProduct() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (productId: number) => api.del<void>(`/admin/products/${productId}`),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useRestoreProduct() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (productId: number) => api.post<void>(`/admin/products/${productId}/restore`),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

/**
 * Uploading and removing a product photograph.
 *
 * The file goes as multipart, so the body is FormData rather than JSON and the
 * browser sets the boundary. Both invalidate the storefront's cache: a card
 * that goes on drawing the cloth after a photo was added is the same bug as one
 * showing a photo the shop removed.
 */
export function useUploadProductImage(productId: number) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return api.post<{ imagePath: string; thumbnailPath: string }>(
        `/admin/products/${productId}/image`, form);
    },
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useRemoveProductImage(productId: number) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: () => api.del<void>(`/admin/products/${productId}/image`),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin'] });
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useSettings() {
  return useQuery({
    queryKey: ['admin', 'settings'],
    queryFn: ({ signal }) => api.get<ShopSettings>('/admin/settings', signal),
  });
}

export function useSaveSettings() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body: ShopSettings) => api.put<void>('/admin/settings', body),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'settings'] });
      // The storefront reads the banner, delivery charge and payment methods
      // from the same row.
      void client.invalidateQueries({ queryKey: ['shop'] });
    },
  });
}
