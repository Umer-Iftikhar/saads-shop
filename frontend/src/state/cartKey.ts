import type { CartItem } from './cart';

/**
 * Identifies a cart line.
 *
 * The same product in two cloths, or two bed sizes, is two lines — a bride
 * ordering a Plum set and a Sage set has not made a mistake.
 *
 * Lives apart from the provider so the module exports only components and Fast
 * Refresh keeps working during development.
 */
export function cartKey(item: Pick<CartItem, 'productId' | 'swatchId' | 'bedSize'>): string {
  return `${item.productId}:${item.swatchId ?? '-'}:${item.bedSize ?? '-'}`;
}
