import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { Field } from './Field';
import { CardSkeleton, EmptyState, ErrorState, Loading } from './Feedback';
import { Swatch, SwatchGroup } from './Swatch';
import { ProductCard } from './ProductCard';
import { StatusTag } from './admin/StatusTag';
import type { ProductSummary } from '../types/api';

const SWATCHES = [
  { swatchId: 1, name: 'Terracotta', colorValue: '#c67139', weave: 'Woven' },
  { swatchId: 2, name: 'Cream',      colorValue: '#f5ead8', weave: 'Striped' },
  { swatchId: 3, name: 'Sage',       colorValue: '#7a8a5e', weave: 'Floral' },
];

describe('Field', () => {
  it('labels its control, so clicking the label focuses the input', async () => {
    render(<Field label="Phone">{props => <input {...props} />}</Field>);

    await userEvent.click(screen.getByText('Phone'));
    expect(screen.getByLabelText('Phone')).toHaveFocus();
  });

  it('ties an error to the control rather than leaving red text floating nearby', () => {
    render(<Field label="Phone" error="That number does not look right.">{props => <input {...props} />}</Field>);

    const input = screen.getByLabelText('Phone');

    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('That number does not look right.');
  });

  it('is not marked invalid when there is no error', () => {
    render(<Field label="Phone">{props => <input {...props} />}</Field>);

    expect(screen.getByLabelText('Phone')).not.toHaveAttribute('aria-invalid');
  });

  it('describes the control with a hint when there is no error', () => {
    render(<Field label="Phone" hint="03xx xxx xxxx">{props => <input {...props} />}</Field>);

    expect(screen.getByLabelText('Phone')).toHaveAccessibleDescription('03xx xxx xxxx');
  });

  it('replaces the hint with the error, so the two do not both speak', () => {
    render(
      <Field label="Phone" hint="03xx xxx xxxx" error="That number does not look right.">
        {props => <input {...props} />}
      </Field>,
    );

    expect(screen.queryByText('03xx xxx xxxx')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Phone')).toHaveAccessibleDescription('That number does not look right.');
  });

  it('gives each field its own ids, so two on one form do not collide', () => {
    render(
      <>
        <Field label="From">{props => <input {...props} />}</Field>
        <Field label="To">{props => <input {...props} />}</Field>
      </>,
    );

    expect(screen.getByLabelText('From')).not.toHaveAttribute('id', screen.getByLabelText('To').id);
  });
});

describe('Feedback states', () => {
  it('announces loading politely rather than interrupting', () => {
    render(<Loading label="Loading orders…" />);

    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Loading orders…');
    expect(status).toHaveAttribute('aria-live', 'polite');
  });

  it('announces an error assertively, because it needs acting on', () => {
    render(<ErrorState title="Could not load orders" />);

    expect(screen.getByRole('alert')).toHaveTextContent('Could not load orders');
  });

  it('offers a retry only when there is something to retry', async () => {
    const onRetry = vi.fn();
    const { rerender } = render(<ErrorState title="Failed" onRetry={onRetry} />);

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledOnce();

    rerender(<ErrorState title="Failed" />);
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  it('shows an empty state with its action', () => {
    render(<EmptyState title="Nothing here yet" detail="Try the wedding sets." action={<button>Go</button>} />);

    expect(screen.getByRole('heading', { name: 'Nothing here yet' })).toBeInTheDocument();
    expect(screen.getByText('Try the wedding sets.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go' })).toBeInTheDocument();
  });

  it('hides skeletons from assistive tech, since they say nothing', () => {
    const { container } = render(<CardSkeleton count={3} />);

    expect(container.querySelectorAll('[aria-hidden="true"]')).toHaveLength(3);
  });
});

describe('Swatch', () => {
  it('is a real button when it can be chosen', () => {
    render(<Swatch name="Terracotta" colorValue="#c67139" weave="Woven" onSelect={() => {}} />);

    // The prototype used a div with onClick: unreachable by keyboard.
    expect(screen.getByRole('button')).toBeInTheDocument();
  });

  it('is an image, not a button, when it is only being shown', () => {
    render(<Swatch name="Terracotta" colorValue="#c67139" weave="Woven" />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByRole('img')).toHaveAccessibleName(/Terracotta/);
  });

  it('says out loud which cloth it is', () => {
    render(<Swatch name="Sage" colorValue="#7a8a5e" weave="Floral" onSelect={() => {}} />);

    expect(screen.getByRole('button')).toHaveAccessibleName(/Sage/);
  });

  it('carries selection in aria-pressed, not only in colour', () => {
    const { rerender } = render(
      <Swatch name="Terracotta" colorValue="#c67139" weave="Woven" selected onSelect={() => {}} />);

    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button')).toHaveAccessibleName(/selected/);

    rerender(<Swatch name="Terracotta" colorValue="#c67139" weave="Woven" onSelect={() => {}} />);
    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'false');
  });

  it('marks the chosen one with a check as well as colour', () => {
    // A colour-blind customer cannot see a ring alone.
    const { container } = render(
      <Swatch name="Terracotta" colorValue="#c67139" weave="Woven" selected onSelect={() => {}} />);

    expect(container.textContent).toContain('✓');
  });

  it('calls back when clicked', async () => {
    const onSelect = vi.fn();
    render(<Swatch name="Terracotta" colorValue="#c67139" weave="Woven" onSelect={onSelect} />);

    await userEvent.click(screen.getByRole('button'));

    expect(onSelect).toHaveBeenCalledOnce();
  });

  it('is reachable by keyboard', async () => {
    const onSelect = vi.fn();
    render(<Swatch name="Terracotta" colorValue="#c67139" weave="Woven" onSelect={onSelect} />);

    await userEvent.tab();
    expect(screen.getByRole('button')).toHaveFocus();

    await userEvent.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalled();
  });
});

describe('SwatchGroup', () => {
  function Group({ initial = 1 }: { initial?: number }) {
    const [selected, setSelected] = useState<number | null>(initial);
    return (
      <SwatchGroup label="Choose the cloth" swatches={SWATCHES}
                   selectedId={selected} onSelect={setSelected} />
    );
  }

  it('is one control, not six tab stops', () => {
    render(<Group />);

    const group = screen.getByRole('radiogroup', { name: 'Choose the cloth' });
    expect(within(group).getAllByRole('button')).toHaveLength(3);
  });

  it('moves to the next cloth with an arrow key', async () => {
    render(<Group />);

    screen.getAllByRole('button')[0].focus();
    await userEvent.keyboard('{ArrowRight}');

    expect(screen.getByRole('button', { name: /Cream/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('moves backwards too', async () => {
    render(<Group initial={2} />);

    screen.getAllByRole('button')[1].focus();
    await userEvent.keyboard('{ArrowLeft}');

    expect(screen.getByRole('button', { name: /Terracotta/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('wraps around at both ends', async () => {
    render(<Group initial={3} />);

    screen.getAllByRole('button')[2].focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('button', { name: /Terracotta/ })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('button', { name: /Sage/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('jumps to the first and last cloth with Home and End', async () => {
    render(<Group initial={2} />);

    screen.getAllByRole('button')[1].focus();

    await userEvent.keyboard('{Home}');
    expect(screen.getByRole('button', { name: /Terracotta/ })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.keyboard('{End}');
    expect(screen.getByRole('button', { name: /Sage/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('leaves other keys to the browser', async () => {
    const onSelect = vi.fn();
    render(<SwatchGroup label="Cloth" swatches={SWATCHES} selectedId={1} onSelect={onSelect} />);

    screen.getAllByRole('button')[0].focus();
    await userEvent.keyboard('a');

    expect(onSelect).not.toHaveBeenCalled();
  });
});

describe('ProductCard', () => {
  const product: ProductSummary = {
    productId: 1,
    name: 'Gulaab Bridal Set',
    slug: 'gulaab-bridal-set',
    categoryName: 'Wedding sets',
    kicker: 'Bridal bedding',
    blurb: 'Fourteen pieces.',
    price: 18_500,
    pieces: '14 pieces',
    inStock: true,
    swatchColorValue: '#c67139',
    swatchWeave: 'Woven',
  };

  const renderCard = (p: ProductSummary, size?: 'featured' | 'listing' | 'compact') =>
    render(<MemoryRouter><ProductCard product={p} size={size} /></MemoryRouter>);

  it('links to the product by slug, not by id', () => {
    renderCard(product);

    expect(screen.getByRole('link')).toHaveAttribute('href', '/product/gulaab-bridal-set');
  });

  it('shows the name and the formatted price', () => {
    renderCard(product);

    expect(screen.getByText('Gulaab Bridal Set')).toBeInTheDocument();
    expect(screen.getByText('Rs 18,500')).toBeInTheDocument();
  });

  it('says when something is out of stock instead of showing the piece count', () => {
    renderCard({ ...product, inStock: false });

    expect(screen.getByText('Out of stock')).toBeInTheDocument();
    expect(screen.queryByText('14 pieces')).not.toBeInTheDocument();
  });

  it('hides the drawn cloth from screen readers, which the name already describes', () => {
    const { container } = renderCard(product);

    expect(container.querySelector('[aria-hidden="true"]')).toBeInTheDocument();
  });

  it('drops the kicker and the blurb on the compact card', () => {
    renderCard(product, 'compact');

    expect(screen.queryByText('Bridal bedding')).not.toBeInTheDocument();
    expect(screen.queryByText('Fourteen pieces.')).not.toBeInTheDocument();
    expect(screen.getByText('Wedding sets')).toBeInTheDocument();
  });

  it('survives a product with no optional copy at all', () => {
    renderCard({ ...product, kicker: null, blurb: null, pieces: null });

    expect(screen.getByText('Gulaab Bridal Set')).toBeInTheDocument();
  });
});

describe('StatusTag', () => {
  it.each(['Placed', 'Measuring', 'Stitching', 'Ready', 'Delivered', 'Cancelled'])(
    'always shows the word %s, not colour alone', status => {
      render(<StatusTag status={status} />);
      expect(screen.getByText(status)).toBeInTheDocument();
    });

  it('renders an unknown status rather than nothing', () => {
    render(<StatusTag status="Something New" />);

    expect(screen.getByText('Something New')).toBeInTheDocument();
  });

  it('gives finished and cancelled work different treatments', () => {
    const { container: delivered } = render(<StatusTag status="Delivered" />);
    const { container: cancelled } = render(<StatusTag status="Cancelled" />);

    expect(delivered.firstElementChild!.className)
      .not.toBe(cancelled.firstElementChild!.className);
  });
});
