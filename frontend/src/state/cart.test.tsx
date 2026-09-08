import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ReactNode } from 'react';
import { CartProvider, useCart } from './cart';
import type { CartItem } from './cart';
import { cartKey } from './cartKey';

const wrapper = ({ children }: { children: ReactNode }) => <CartProvider>{children}</CartProvider>;

function anItem(overrides: Partial<CartItem> = {}): CartItem {
  return {
    productId: 1,
    name: 'Gulaab Bridal Set',
    price: 18_500,
    slug: 'gulaab-bridal-set',
    quantity: 1,
    swatchId: 1,
    swatchName: 'Terracotta',
    bedSize: 'Double',
    ...overrides,
  };
}

const useCartInProvider = () => renderHook(() => useCart(), { wrapper });

describe('cart lines', () => {
  it('starts empty', () => {
    const { result } = useCartInProvider();

    expect(result.current.items).toEqual([]);
    expect(result.current.count).toBe(0);
    expect(result.current.subtotal).toBe(0);
  });

  it('adds an item', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));

    expect(result.current.items).toHaveLength(1);
    expect(result.current.count).toBe(1);
  });

  it('merges the same product in the same cloth and size into one line', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));
    act(() => result.current.add(anItem({ quantity: 2 })));

    expect(result.current.items).toHaveLength(1);
    expect(result.current.items[0].quantity).toBe(3);
  });

  it('keeps the same product in a different cloth as its own line', () => {
    const { result } = useCartInProvider();

    // A bride ordering a Plum set and a Sage set has not made a mistake.
    act(() => result.current.add(anItem({ swatchId: 1, swatchName: 'Terracotta' })));
    act(() => result.current.add(anItem({ swatchId: 5, swatchName: 'Plum' })));

    expect(result.current.items).toHaveLength(2);
  });

  it('keeps the same product in a different bed size as its own line', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ bedSize: 'Double' })));
    act(() => result.current.add(anItem({ bedSize: 'King' })));

    expect(result.current.items).toHaveLength(2);
  });

  it('counts every piece, not every line', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ quantity: 2 })));
    act(() => result.current.add(anItem({ productId: 2, swatchId: 2, quantity: 3 })));

    expect(result.current.items).toHaveLength(2);
    expect(result.current.count).toBe(5);
  });

  it('adds up an estimate from the prices it was shown', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ price: 18_500, quantity: 2 })));
    act(() => result.current.add(anItem({ productId: 2, swatchId: 2, price: 3_000, quantity: 1 })));

    expect(result.current.subtotal).toBe(40_000);
  });
});

describe('changing what is in the cart', () => {
  it('sets a quantity', () => {
    const { result } = useCartInProvider();
    const item = anItem();

    act(() => result.current.add(item));
    act(() => result.current.setQuantity(cartKey(item), 7));

    expect(result.current.items[0].quantity).toBe(7);
  });

  it('removes the line when the quantity reaches zero', () => {
    const { result } = useCartInProvider();
    const item = anItem();

    act(() => result.current.add(item));
    act(() => result.current.setQuantity(cartKey(item), 0));

    expect(result.current.items).toEqual([]);
  });

  it('removes the line for a negative quantity rather than storing one', () => {
    const { result } = useCartInProvider();
    const item = anItem();

    act(() => result.current.add(item));
    act(() => result.current.setQuantity(cartKey(item), -3));

    expect(result.current.items).toEqual([]);
  });

  it('caps a quantity rather than letting someone order 10,000 sheets by typo', () => {
    const { result } = useCartInProvider();
    const item = anItem();

    act(() => result.current.add(item));
    act(() => result.current.setQuantity(cartKey(item), 100_000));

    expect(result.current.items[0].quantity).toBe(999);
  });

  it('caps the merged quantity too', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ quantity: 900 })));
    act(() => result.current.add(anItem({ quantity: 900 })));

    expect(result.current.items[0].quantity).toBe(999);
  });

  it('removes one line and leaves the rest', () => {
    const { result } = useCartInProvider();
    const first = anItem();

    act(() => result.current.add(first));
    act(() => result.current.add(anItem({ productId: 2, swatchId: 2 })));
    act(() => result.current.remove(cartKey(first)));

    expect(result.current.items).toHaveLength(1);
    expect(result.current.items[0].productId).toBe(2);
  });

  it('ignores a key that is not in the cart', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));
    act(() => result.current.remove('999:-:-'));

    expect(result.current.items).toHaveLength(1);
  });

  it('empties itself after an order is placed', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));
    act(() => result.current.clear());

    expect(result.current.items).toEqual([]);
    expect(result.current.count).toBe(0);
  });

  it('refuses to grow past a sane number of lines', () => {
    const { result } = useCartInProvider();

    act(() => {
      for (let i = 0; i < 60; i++) result.current.add(anItem({ productId: i, swatchId: i }));
    });

    expect(result.current.items).toHaveLength(50);
  });
});

describe('what the cart sends to the server', () => {
  it('sends what was chosen and no prices at all', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ quantity: 2 })));

    const [line] = result.current.toRequestLines();

    // A price in the request is a price the customer could edit.
    expect(line).toEqual({ productId: 1, quantity: 2, swatchId: 1, bedSize: 'Double' });
    expect(line).not.toHaveProperty('price');
    expect(line).not.toHaveProperty('name');
  });

  it('sends null rather than undefined for an absent cloth or size', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem({ swatchId: undefined, bedSize: undefined })));

    expect(result.current.toRequestLines()[0]).toEqual({
      productId: 1, quantity: 1, swatchId: null, bedSize: null,
    });
  });

  it('sends one entry per line', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));
    act(() => result.current.add(anItem({ productId: 2, swatchId: 2 })));

    expect(result.current.toRequestLines()).toHaveLength(2);
  });
});

describe('surviving a reload', () => {
  it('writes the cart to storage', () => {
    const { result } = useCartInProvider();

    act(() => result.current.add(anItem()));

    expect(JSON.parse(localStorage.getItem('saadsshop.cart.v1')!)).toHaveLength(1);
  });

  it('reads a cart back', () => {
    const { result: first } = useCartInProvider();
    act(() => first.current.add(anItem({ quantity: 3 })));

    const { result: second } = useCartInProvider();

    expect(second.current.items).toHaveLength(1);
    expect(second.current.count).toBe(3);
  });

  it('ignores stored rubbish rather than crashing the shop', () => {
    localStorage.setItem('saadsshop.cart.v1', 'not json at all');

    const { result } = useCartInProvider();

    expect(result.current.items).toEqual([]);
  });

  it('ignores stored JSON that is not an array', () => {
    localStorage.setItem('saadsshop.cart.v1', '{"productId":1}');

    expect(useCartInProvider().result.current.items).toEqual([]);
  });

  it('drops hand-edited entries that no longer look like cart lines', () => {
    localStorage.setItem('saadsshop.cart.v1', JSON.stringify([
      anItem(),
      { productId: 'not a number', quantity: 1 },
      { productId: 2 },
      { productId: 3, quantity: 0 },
      { productId: 4, quantity: -1 },
      null,
    ]));

    const { result } = useCartInProvider();

    expect(result.current.items).toHaveLength(1);
    expect(result.current.items[0].productId).toBe(1);
  });
});

describe('cart keys', () => {
  it('is the same for the same product, cloth and size', () => {
    expect(cartKey({ productId: 1, swatchId: 2, bedSize: 'King' }))
      .toBe(cartKey({ productId: 1, swatchId: 2, bedSize: 'King' }));
  });

  it.each([
    [{ productId: 2, swatchId: 2, bedSize: 'King' }],
    [{ productId: 1, swatchId: 3, bedSize: 'King' }],
    [{ productId: 1, swatchId: 2, bedSize: 'Double' }],
  ])('differs when any of the three differs: %j', other => {
    expect(cartKey({ productId: 1, swatchId: 2, bedSize: 'King' })).not.toBe(cartKey(other));
  });

  it('treats a missing cloth and a missing size consistently', () => {
    expect(cartKey({ productId: 1, swatchId: null, bedSize: null }))
      .toBe(cartKey({ productId: 1, swatchId: undefined, bedSize: undefined }));
  });
});

describe('using the cart outside its provider', () => {
  it('fails loudly rather than silently doing nothing', () => {
    expect(() => renderHook(() => useCart())).toThrow(/CartProvider/);
  });
});
