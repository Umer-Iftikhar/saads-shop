import { describe, expect, it } from 'vitest';
import {
  fromApiErrors, hasErrors, isValidPhone, normalisePhone,
  validateCheckout, validateDateRange, validateNotFuture,
} from './validation';

/** An `<input type="date">` value, N days from today. */
function day(offset: number): string {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() + offset);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

describe('phone numbers', () => {
  it.each([
    '03012345678', '0301 234 5678', '0301-234-5678',
    '+923012345678', '+92 301 234 5678', '923012345678', '3012345678',
  ])('accepts %s', input => expect(isValidPhone(input)).toBe(true));

  it.each([
    '', '   ', '0301234567', '030123456789', '04012345678',
    'not a number', '+913012345678',
  ])('rejects %s', input => expect(isValidPhone(input)).toBe(false));

  it('trims before testing, because people paste with spaces', () => {
    expect(isValidPhone('  03012345678  ')).toBe(true);
  });

  it.each([
    ['03012345678',      '03012345678'],
    ['0301 234 5678',    '03012345678'],
    ['0301-234-5678',    '03012345678'],
    ['(0301) 234.5678',  '03012345678'],
    ['+923012345678',    '03012345678'],
    ['00923012345678',   '03012345678'],
    ['923012345678',     '03012345678'],
    ['3012345678',       '03012345678'],
  ])('normalises %s to %s', (input, expected) => {
    expect(normalisePhone(input)).toBe(expected);
  });

  it('collapses every spelling of one number to a single value', () => {
    const spellings = ['03451112233', '0345 111 2233', '+923451112233', '3451112233'];
    expect(new Set(spellings.map(normalisePhone)).size).toBe(1);
  });

  it.each(['12345', 'abcdefghijk', '04012345678', ''])(
    'returns null for %s rather than a half-normalised string',
    input => expect(normalisePhone(input)).toBeNull(),
  );

  it('mirrors the server: the same inputs pass both the pattern and the normaliser', () => {
    // A client that accepts what the server rejects produces a confusing error.
    for (const input of ['03012345678', '+92 301 234 5678', '3012345678']) {
      expect(isValidPhone(input)).toBe(true);
      expect(normalisePhone(input)).not.toBeNull();
    }
  });
});

describe('date ranges — the browser twin of the server attribute', () => {
  it('passes an ordinary range', () => {
    expect(validateDateRange(day(-30), day(0))).toEqual({});
  });

  it('passes when both ends are blank, meaning no filter', () => {
    expect(validateDateRange('', '')).toEqual({});
  });

  it('refuses an empty range where one is required', () => {
    expect(validateDateRange('', '', { allowOpenRange: false })).toEqual({
      fromDate: 'Choose a date range.',
    });
  });

  it('passes with only a start', () => {
    expect(validateDateRange(day(-7), '')).toEqual({});
  });

  it('passes with only an end', () => {
    expect(validateDateRange('', day(0))).toEqual({});
  });

  it('passes a single day, because the range includes both ends', () => {
    expect(validateDateRange(day(0), day(0))).toEqual({});
  });

  it('refuses a backwards range and blames the start date', () => {
    expect(validateDateRange(day(0), day(-1))).toEqual({
      fromDate: 'The start date must be on or before the end date.',
    });
  });

  it('refuses a future start date', () => {
    expect(validateDateRange(day(1), day(2)).fromDate).toBe('The start date cannot be in the future.');
  });

  it('refuses a future end date', () => {
    expect(validateDateRange(day(-1), day(1)).toDate).toBe('The end date cannot be in the future.');
  });

  it('allows the future where scheduling needs it', () => {
    expect(validateDateRange(day(3), day(10), { allowFuture: true })).toEqual({});
  });

  it('passes a span exactly at the limit', () => {
    expect(validateDateRange(day(-364), day(0), { maxSpanDays: 365 })).toEqual({});
  });

  it('refuses one day past the limit and blames the end date', () => {
    const errors = validateDateRange(day(-365), day(0), { maxSpanDays: 365 });
    expect(errors.toDate).toContain('366 days');
    expect(errors.toDate).toContain('365 days or fewer');
  });

  it('admits five years on the customer screen', () => {
    expect(validateDateRange(day(-1829), day(0), { maxSpanDays: 1830 })).toEqual({});
  });

  it('refuses a typo year rather than scanning the whole history', () => {
    expect(validateDateRange('0202-01-01', day(0)).fromDate).toContain('1 Jan 2000');
  });

  it('takes a configurable floor', () => {
    expect(validateDateRange('2019-06-01', day(0), { earliestYear: 2020, maxSpanDays: 99_999 }).fromDate)
      .toContain('1 Jan 2020');
    expect(validateDateRange('2021-06-01', day(0), { earliestYear: 2020, maxSpanDays: 99_999 }))
      .toEqual({});
  });

  it('reports an unreadable date rather than guessing', () => {
    expect(validateDateRange('not-a-date', '').fromDate).toBe('That is not a date we can read.');
    expect(validateDateRange('', 'nonsense').toDate).toBe('That is not a date we can read.');
  });

  it('does not go on to compare a range it could not parse', () => {
    const errors = validateDateRange('not-a-date', day(-500));
    expect(errors.fromDate).toBe('That is not a date we can read.');
    expect(errors.toDate).toBeUndefined();
  });

  it('keys errors by field so a form can place each one under its input', () => {
    expect(Object.keys(validateDateRange(day(0), day(-1)))).toEqual(['fromDate']);
  });

  it('reports both ends at once where the server would report only the first', () => {
    //  A deliberate difference. The server returns one ValidationResult; the
    //  browser renders a message under each input, so showing both is more
    //  useful. It is safe in the direction that matters: the client is stricter
    //  here, never laxer, so nothing it accepts can be refused by the server.
    const errors = validateDateRange(day(1), day(2));

    expect(errors.fromDate).toBe('The start date cannot be in the future.');
    expect(errors.toDate).toBe('The end date cannot be in the future.');
  });
});

describe('a single date that must not be in the future', () => {
  it('passes an empty value, which is Required-s business', () => {
    expect(validateNotFuture('')).toBeNull();
  });

  it('passes today and yesterday', () => {
    expect(validateNotFuture(day(0))).toBeNull();
    expect(validateNotFuture(day(-1))).toBeNull();
  });

  it('refuses tomorrow, naming the field', () => {
    expect(validateNotFuture(day(1), 'Delivered on')).toBe('Delivered on cannot be in the future.');
  });

  it('reports an unreadable date', () => {
    expect(validateNotFuture('rubbish')).toBe('That is not a date we can read.');
  });
});

describe('checkout', () => {
  const valid = {
    customerName: 'Hina Aslam',
    phone: '0301 234 5678',
    deliveryAddress: 'House 4, Street 7, Satellite Town',
  };

  it('passes a filled form', () => {
    expect(validateCheckout(valid)).toEqual({});
  });

  it('reports every empty field at once rather than one at a time', () => {
    const errors = validateCheckout({ customerName: '', phone: '', deliveryAddress: '' });
    expect(Object.keys(errors).toSorted()).toEqual(['customerName', 'deliveryAddress', 'phone']);
  });

  it.each([
    ['', 'Please tell us your name.'],
    ['  ', 'Please tell us your name.'],
    ['H', 'That name looks too short.'],
  ])('refuses the name %j', (customerName, message) => {
    expect(validateCheckout({ ...valid, customerName }).customerName).toBe(message);
  });

  it('refuses a name longer than the column', () => {
    expect(validateCheckout({ ...valid, customerName: 'a'.repeat(129) }).customerName)
      .toBe('That name is too long.');
  });

  it('accepts a name at exactly the limit', () => {
    expect(validateCheckout({ ...valid, customerName: 'a'.repeat(128) }).customerName).toBeUndefined();
  });

  it('tells the customer the shape a phone number should take', () => {
    expect(validateCheckout({ ...valid, phone: '12345' }).phone)
      .toBe('That phone number does not look right. Use the form 03xx xxx xxxx.');
  });

  it('distinguishes a missing phone from a malformed one', () => {
    expect(validateCheckout({ ...valid, phone: '' }).phone).toBe('A phone number is required.');
  });

  it.each([
    ['', 'Please give an address in Rawalpindi.'],
    ['H 4', 'That address looks too short.'],
  ])('refuses the address %j', (deliveryAddress, message) => {
    expect(validateCheckout({ ...valid, deliveryAddress }).deliveryAddress).toBe(message);
  });

  it('refuses an address longer than the column', () => {
    expect(validateCheckout({ ...valid, deliveryAddress: 'a'.repeat(401) }).deliveryAddress)
      .toBe('That address is too long.');
  });

  it('trims before measuring, so spaces are not a name', () => {
    expect(validateCheckout({ ...valid, customerName: '        ' }).customerName)
      .toBe('Please tell us your name.');
  });
});

describe('hasErrors', () => {
  it('is false for an empty map and true for a populated one', () => {
    expect(hasErrors({})).toBe(false);
    expect(hasErrors({ phone: 'bad' })).toBe(true);
  });
});

describe('server-side errors folded into the local shape', () => {
  it('lower-cases the first letter, because the API sends PascalCase', () => {
    expect(fromApiErrors({ CustomerName: ['Too short.'] })).toEqual({ customerName: 'Too short.' });
  });

  it('keeps only the first message per field, which is what a form can show', () => {
    expect(fromApiErrors({ Phone: ['First.', 'Second.'] })).toEqual({ phone: 'First.' });
  });

  it('skips fields with no messages rather than rendering an empty error', () => {
    expect(fromApiErrors({ Phone: [] })).toEqual({});
  });

  it('handles several fields', () => {
    expect(fromApiErrors({ Phone: ['a'], DeliveryAddress: ['b'] }))
      .toEqual({ phone: 'a', deliveryAddress: 'b' });
  });

  it('leaves an already-camelCase key alone', () => {
    expect(fromApiErrors({ phone: ['a'] })).toEqual({ phone: 'a' });
  });

  it('produces the same shape the local validators do, so one path renders both', () => {
    const local  = validateCheckout({ customerName: '', phone: '0301 234 5678', deliveryAddress: 'House 4, Street 7' });
    const server = fromApiErrors({ CustomerName: ['Please tell us your name.'] });

    expect(Object.keys(local)).toEqual(Object.keys(server));
  });
});
