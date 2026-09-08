/**
 * An order status or a stitching stage as a tag.
 *
 * The design distinguished these by colour alone. The text label is kept
 * alongside — a colour-blind shopkeeper reading a column of tags needs to know
 * which is Delivered and which is Cancelled without matching hues.
 */
const TONE: Record<string, string> = {
  Delivered: 'tag tag-accent-2',
  Ready:     'tag tag-accent-2',
  Cancelled: 'tag tag-neutral',
  Placed:    'tag tag-accent',
  Measuring: 'tag tag-accent',
  Stitching: 'tag tag-accent',
  Cutting:   'tag tag-accent',
  Done:      'tag tag-neutral',
};

export function StatusTag({ status }: { status: string }) {
  return <span className={TONE[status] ?? 'tag tag-neutral'}>{status}</span>;
}
