import { useQuery } from '@tanstack/react-query';
import { api, queryString } from '../lib/api';
import type {
  BedSize, Category, PagedResponse, ProductDetail, ProductSummary, ShopSettingsPublic, Swatch,
} from '../types/api';

/**
 * Query hooks.
 *
 * Stale times are set per resource rather than globally: the cloth palette
 * changes about once a season, the catalogue a few times a week, and a stale
 * price on a product page would be a price the customer is not charged.
 */

const HOUR = 60 * 60 * 1000;
const MINUTE = 60 * 1000;

export function useShopSettings() {
  return useQuery({
    queryKey: ['shop', 'settings'],
    queryFn: ({ signal }) => api.get<ShopSettingsPublic>('/shop/settings', signal),
    staleTime: 30 * MINUTE,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: ['catalog', 'categories'],
    queryFn: ({ signal }) => api.get<Category[]>('/catalog/categories', signal),
    staleTime: HOUR,
  });
}

export function useSwatches() {
  return useQuery({
    queryKey: ['catalog', 'swatches'],
    queryFn: ({ signal }) => api.get<Swatch[]>('/catalog/swatches', signal),
    staleTime: HOUR,
  });
}

export function useBedSizes() {
  return useQuery({
    queryKey: ['catalog', 'bed-sizes'],
    queryFn: ({ signal }) => api.get<BedSize[]>('/catalog/bed-sizes', signal),
    staleTime: HOUR,
  });
}

export interface ProductQuery {
  category?: string;
  search?: string;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export function useProducts(query: ProductQuery = {}) {
  return useQuery({
    queryKey: ['catalog', 'products', query],
    queryFn: ({ signal }) =>
      api.get<PagedResponse<ProductSummary>>(`/catalog/products${queryString({ ...query })}`, signal),
    staleTime: 5 * MINUTE,
    // Keeps the previous page on screen while the next loads, so paging and
    // filtering do not blank the grid and jump the scroll position.
    placeholderData: previous => previous,
  });
}

export function useProduct(slug: string | undefined) {
  return useQuery({
    queryKey: ['catalog', 'product', slug],
    queryFn: ({ signal }) => api.get<ProductDetail>(`/catalog/products/by-slug/${slug}`, signal),
    enabled: Boolean(slug),
    staleTime: 2 * MINUTE,
  });
}
