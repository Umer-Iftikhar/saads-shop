import { fabricBackground, fabricLabel } from '../lib/fabric';

interface SwatchProps {
  name: string;
  colorValue: string;
  weave?: string | null;
  size?: number;
  selected?: boolean;
  onSelect?: () => void;
}

/**
 * A cloth swatch.
 *
 * A real `<button>` with an accessible name, not the prototype's `div` with an
 * `onClick` — that div was unreachable by keyboard and silent to a screen
 * reader. Selection is shown by a ring *and* a check mark, because the
 * prototype signalled it with colour alone, which a colour-blind customer
 * cannot see.
 */
export function Swatch({ name, colorValue, weave, size = 54, selected = false, onSelect }: SwatchProps) {
  const style: React.CSSProperties = {
    width: size,
    height: size,
    borderRadius: '50%',
    background: fabricBackground(colorValue, weave),
    boxShadow: selected ? '0 0 0 3px var(--color-accent)' : 'var(--shadow-sm)',
    border: 'none',
    cursor: onSelect ? 'pointer' : 'default',
    display: 'grid',
    placeItems: 'center',
    padding: 0,
  };

  const content = (
    <span
      aria-hidden="true"
      style={{
        color: '#f5ead8',
        fontSize: size * 0.42,
        lineHeight: 1,
        textShadow: '0 1px 2px rgba(46,43,37,.45)',
        visibility: selected ? 'visible' : 'hidden',
      }}
    >
      ✓
    </span>
  );

  if (!onSelect) {
    return <span className="washed" style={style} role="img" aria-label={fabricLabel(name, weave)}>{content}</span>;
  }

  return (
    <button
      type="button"
      className="washed"
      style={style}
      onClick={onSelect}
      aria-pressed={selected}
      aria-label={fabricLabel(name, weave, selected)}
    >
      {content}
    </button>
  );
}

/**
 * A row of swatches behaving as one control: arrow keys move between them, and
 * only the selected one is in the tab order, so a keyboard user tabs past the
 * group rather than through six separate stops.
 */
export function SwatchGroup({
  label,
  swatches,
  selectedId,
  onSelect,
  size = 54,
}: {
  label: string;
  swatches: { swatchId: number; name: string; colorValue: string; weave?: string | null }[];
  selectedId: number | null;
  onSelect: (id: number) => void;
  size?: number;
}) {
  function onKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    const keys = ['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp', 'Home', 'End'];
    if (!keys.includes(event.key)) return;

    event.preventDefault();
    const index = swatches.findIndex(s => s.swatchId === selectedId);
    const current = index === -1 ? 0 : index;

    const next =
      event.key === 'Home' ? 0 :
      event.key === 'End'  ? swatches.length - 1 :
      event.key === 'ArrowRight' || event.key === 'ArrowDown'
        ? (current + 1) % swatches.length
        : (current - 1 + swatches.length) % swatches.length;

    onSelect(swatches[next].swatchId);
  }

  return (
    <div
      role="radiogroup"
      aria-label={label}
      onKeyDown={onKeyDown}
      style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}
    >
      {swatches.map(swatch => (
        <Swatch
          key={swatch.swatchId}
          name={swatch.name}
          colorValue={swatch.colorValue}
          weave={swatch.weave}
          size={size}
          selected={swatch.swatchId === selectedId}
          onSelect={() => onSelect(swatch.swatchId)}
        />
      ))}
    </div>
  );
}
