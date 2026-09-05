import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import type { CartLineRequest } from '../types/api';
import { cartKey } from './cartKey';

/**
 * The cart.
 *
 * Held in the browser and sent whole at checkout, because the shop takes cash
 * on delivery — there is no account to hang a server-side cart on, and no
 * reason to make someone sign in to browse.
 *
 * What is stored is deliberately minimal: what was chosen, not what it costs.
 * A price kept here would be a price the customer could edit, and it would go
 * stale the moment the shop changed it. The cart shows prices fetched from the
 * catalogue, and checkout is priced by the database under lock.
 */

export interface CartItem {
  productId: number;
  /** Snapshotted for display only — never sent, never trusted. */
  name: string;
  price: number;
  slug: string;
  quantity: number;
  swatchId?: number | null;
  swatchName?: string | null;
  swatchColorValue?: string | null;
  swatchWeave?: string | null;
  bedSize?: string | null;
  pieces?: string | null;
}

interface CartContextValue {
  items: CartItem[];
  count: number;
  subtotal: number;
  add: (item: CartItem) => void;
  setQuantity: (key: string, quantity: number) => void;
  remove: (key: string) => void;
  clear: () => void;
  toRequestLines: () => CartLineRequest[];
}

const STORAGE_KEY = 'saadsshop.cart.v1';
const MAX_QUANTITY = 999;
const MAX_LINES = 50;

const CartContext = createContext<CartContextValue | null>(null);

function load(): CartItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];

    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];

    // Anything in localStorage may have been edited by hand or left by an older
    // version of the app. Keep only entries that still look like cart lines.
    return parsed.filter((item): item is CartItem =>
      typeof item === 'object' && item !== null &&
      typeof (item as CartItem).productId === 'number' &&
      typeof (item as CartItem).quantity === 'number' &&
      (item as CartItem).quantity > 0);
  } catch {
    // A private window, cleared site data, or a browser refusing storage.
    // An empty cart is the right answer; a crash is not.
    return [];
  }
}

export function CartProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<CartItem[]>(load);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    } catch {
      // Storage full or blocked. The cart still works for this visit; losing it
      // on reload is better than failing the add.
    }
  }, [items]);

  const add = useCallback((item: CartItem) => {
    setItems(current => {
      const key = cartKey(item);
      const existing = current.find(i => cartKey(i) === key);

      if (existing) {
        return current.map(i => cartKey(i) === key
          ? { ...i, quantity: Math.min(i.quantity + item.quantity, MAX_QUANTITY) }
          : i);
      }

      if (current.length >= MAX_LINES) return current;

      return [...current, { ...item, quantity: Math.min(item.quantity, MAX_QUANTITY) }];
    });
  }, []);

  const setQuantity = useCallback((key: string, quantity: number) => {
    setItems(current => quantity <= 0
      ? current.filter(i => cartKey(i) !== key)
      : current.map(i => cartKey(i) === key
          ? { ...i, quantity: Math.min(quantity, MAX_QUANTITY) }
          : i));
  }, []);

  const remove = useCallback((key: string) => {
    setItems(current => current.filter(i => cartKey(i) !== key));
  }, []);

  const clear = useCallback(() => setItems([]), []);

  const value = useMemo<CartContextValue>(() => ({
    items,
    count: items.reduce((sum, i) => sum + i.quantity, 0),
    // Display only. The binding total is the one the database computes.
    subtotal: items.reduce((sum, i) => sum + i.price * i.quantity, 0),
    add,
    setQuantity,
    remove,
    clear,
    toRequestLines: () => items.map(i => ({
      productId: i.productId,
      quantity: i.quantity,
      swatchId: i.swatchId ?? null,
      bedSize: i.bedSize ?? null,
    })),
  }), [items, add, setQuantity, remove, clear]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart(): CartContextValue {
  const context = useContext(CartContext);
  if (!context) throw new Error('useCart must be used inside a CartProvider');
  return context;
}
