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
  /*  The ring and the cloth are the two things a utility class cannot know:
      the ring depends on selection, the cloth is generated per colour, and the
      size is a prop. Everything else is a class.                            */
  const className = `washed grid place-items-center rounded-full border-0 p-0
                     ${onSelect ? 'cursor-pointer' : 'cursor-default'}
                     ${selected ? 'shadow-[0_0_0_3px_var(--color-accent)]' : 'shadow-sm'}`;

  const style: React.CSSProperties = {
    width: size,
    height: size,
    background: fabricBackground(colorValue, weave),
  };

  const content = (
    <span
      aria-hidden="true"
      className={`leading-none text-bg [text-shadow:0_1px_2px_rgba(46,43,37,.45)]
                  ${selected ? 'visible' : 'invisible'}`}
      style={{ fontSize: size * 0.42 }}
    >
      ✓
    </span>
  );

  if (!onSelect) {
    return (
      <span className={className} style={style} role="img" aria-label={fabricLabel(name, weave)}>
        {content}
      </span>
    );
  }

  return (
    <button
      type="button"
      className={className}
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
      className="flex flex-wrap gap-3"
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
