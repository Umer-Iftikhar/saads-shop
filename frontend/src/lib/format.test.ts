import { describe, expect, it } from 'vitest';
import {
  formatLongDate, formatPhone, formatPkr, formatShortDate, toDateInput, whatsAppLink,
} from './format';
import { fabricBackground, fabricLabel } from './fabric';

describe('money', () => {
  it.each([
    [18500, 'Rs 18,500'],
    [0, 'Rs 0'],
    [300, 'Rs 300'],
    [1_000_000, 'Rs 1,000,000'],
  ])('writes %d as %s', (amount, expected) => expect(formatPkr(amount)).toBe(expected));

  it('rounds to whole rupees, because the bazaar does not deal in paisa', () => {
    expect(formatPkr(18_500.49)).toBe('Rs 18,500');
    expect(formatPkr(18_500.5)).toBe('Rs 18,501');
  });

  it.each([null, undefined, NaN])('shows Rs 0 for %s rather than "Rs NaN"', amount => {
    expect(formatPkr(amount as number)).toBe('Rs 0');
  });

  it('handles a negative adjustment', () => {
    expect(formatPkr(-1500)).toBe('Rs -1,500');
  });
});

describe('dates', () => {
  //  Asserted by shape rather than by exact string: the month abbreviation and
  //  the separators come from the platform's ICU data ("Sep" in a browser,
  //  "Sept" in this Node build), and pinning one spelling would fail on the
  //  other without anything being wrong.
  it('writes a short date as day and month, with no year and no time', () => {
    const formatted = formatShortDate('2026-09-04T10:00:00Z');

    expect(formatted).toMatch(/^4 Sep/);
    expect(formatted).not.toContain('2026');
    expect(formatted).not.toContain(':');
  });

  it('accepts a Date as well as a string', () => {
    expect(formatShortDate(new Date('2026-09-04T10:00:00Z')))
      .toBe(formatShortDate('2026-09-04T10:00:00Z'));
  });

  it('writes a long date with the weekday spelled out', () => {
    const formatted = formatLongDate('2026-09-04T10:00:00Z');

    expect(formatted).toContain('Friday');
    expect(formatted).toContain('September');
    expect(formatted).toContain('4');
  });

  it.each([null, undefined, ''])('shows an em dash for %s rather than "Invalid Date"', value => {
    expect(formatShortDate(value as string)).toBe('—');
  });

  it('shows an em dash for an unparseable string', () => {
    expect(formatShortDate('not a date')).toBe('—');
  });

  it('returns an empty long date rather than an em dash, because it sits in a sentence', () => {
    expect(formatLongDate(null)).toBe('');
    expect(formatLongDate('rubbish')).toBe('');
  });

  it('produces the value an input type=date will take', () => {
    expect(toDateInput(new Date('2026-09-08T13:45:00Z'))).toBe('2026-09-08');
    expect(toDateInput(new Date('2026-09-08T13:45:00Z'))).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

describe('phone numbers on screen', () => {
  it('groups a local number for reading aloud', () => {
    expect(formatPhone('03012345678')).toBe('0301 234 5678');
  });

  it('strips whatever punctuation was stored before grouping', () => {
    expect(formatPhone('0301-234-5678')).toBe('0301 234 5678');
  });

  it('leaves something it does not recognise alone rather than mangling it', () => {
    expect(formatPhone('+1 555 0100')).toBe('+1 555 0100');
  });

  it.each([null, undefined, ''])('renders nothing for %s', value => {
    expect(formatPhone(value as string)).toBe('');
  });
});

describe('WhatsApp links', () => {
  it('converts the local number to the international form wa.me needs', () => {
    expect(whatsAppLink('03012345678')).toBe('https://wa.me/923012345678');
  });

  it('strips punctuation from the number', () => {
    expect(whatsAppLink('0301 234 5678')).toBe('https://wa.me/923012345678');
  });

  it('leaves an already-international number alone', () => {
    expect(whatsAppLink('923012345678')).toBe('https://wa.me/923012345678');
  });

  it('encodes the message so a comma or a newline cannot break the URL', () => {
    const link = whatsAppLink('03012345678', 'Assalam o alaikum! Order SS-2419 — is it ready?');

    expect(link).toContain('?text=');
    expect(link).not.toContain(' ');
    expect(decodeURIComponent(link.split('?text=')[1]))
      .toBe('Assalam o alaikum! Order SS-2419 — is it ready?');
  });

  it('omits the query entirely when there is no message', () => {
    expect(whatsAppLink('03012345678')).not.toContain('?');
  });
});

describe('fabric, which is drawn rather than photographed', () => {
  it('draws each weave differently', () => {
    const woven   = fabricBackground('#c67139', 'Woven');
    const striped = fabricBackground('#c67139', 'Striped');
    const floral  = fabricBackground('#c67139', 'Floral');

    expect(new Set([woven, striped, floral]).size).toBe(3);
  });

  it('interpolates the colour as stored, so oklch survives untouched', () => {
    // Converting the OKLCH values to hex would shift Gold and Plum.
    expect(fabricBackground('oklch(0.8 0.12 85)', 'Woven')).toContain('oklch(0.8 0.12 85)');
  });

  it('falls back to a neutral rather than producing invalid CSS', () => {
    expect(fabricBackground(null, 'Woven')).toContain('var(--color-neutral-400)');
    expect(fabricBackground('   ', 'Woven')).toContain('var(--color-neutral-400)');
  });

  it('treats an unknown weave as woven rather than rendering nothing', () => {
    expect(fabricBackground('#c67139', 'Jacquard')).toBe(fabricBackground('#c67139', 'Woven'));
    expect(fabricBackground('#c67139', null)).toBe(fabricBackground('#c67139', 'Woven'));
  });

  it('names a cloth for a screen reader', () => {
    expect(fabricLabel('Terracotta', 'Woven')).toContain('Terracotta');
  });

  it('says out loud when a cloth is the chosen one', () => {
    const selected   = fabricLabel('Terracotta', 'Woven', true);
    const unselected = fabricLabel('Terracotta', 'Woven', false);

    expect(selected).toContain('selected');
    expect(unselected).not.toContain('selected');
  });
});
