import { Link } from 'react-router-dom';
import { ErrorState, Loading } from '../../components/Feedback';
import { StatusTag } from '../../components/admin/StatusTag';
import { fabricBackground } from '../../lib/fabric';
import { formatLongDate, formatPkr, formatShortDate } from '../../lib/format';
import { useDashboard } from '../../hooks/adminQueries';

/**
 * Frame 07 — Overview.
 *
 * Four stat tiles, twelve weeks of sales, best sellers and the latest orders.
 * Every number comes from the dashboard procedure; nothing is computed here,
 * so the panel and any future report cannot disagree.
 */
export function Overview() {
  const dashboard = useDashboard();

  if (dashboard.isPending) return <Loading label="Reading today's numbers…" />;

  if (dashboard.isError || !dashboard.data) {
    return (
      <ErrorState
        title="Could not load the overview"
        detail="The shop is fine — this is the panel's end."
        onRetry={() => dashboard.refetch()}
      />
    );
  }

  const { stats, salesChart, bestSellers, latestOrders } = dashboard.data;

  // The chart scales to its own tallest week. A fixed maximum would flatten a
  // quiet month into a row of stubs and hide the shape entirely.
  const peak = Math.max(...salesChart.map(w => w.sales), 1);

  const change = stats.salesChangePercent;

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Today at the shop</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {formatLongDate(new Date())}
            {stats.jobsOnFloor > 4 ? ' · shaadi season' : ''}
          </div>
        </div>

        <Link to="/shop-panel/products/new" className="btn btn-primary">+ New product</Link>
      </header>

      <div className="mb-5 grid grid-cols-1 gap-4 sm:grid-cols-2 3xl:grid-cols-4">
        <StatTile
          label="Sales today"
          value={formatPkr(stats.salesToday)}
          delta={
            change === null
              ? 'No sales this day last week'
              : `${change >= 0 ? '+' : ''}${change}% on the same day last week`
          }
          tone={change === null ? 'neutral' : change >= 0 ? 'good' : 'warn'}
        />
        <StatTile
          label="Orders open"
          value={String(stats.ordersOpen)}
          delta={`${stats.ordersAwaitingMeasurements} waiting on measurements`}
          tone="warn"
        />
        <StatTile
          label="On the stitching floor"
          value={String(stats.jobsOnFloor)}
          delta={stats.jobsDueTomorrow ? `${stats.jobsDueTomorrow} due tomorrow` : 'Nothing due tomorrow'}
          tone="neutral"
        />
        <StatTile
          label="This month so far"
          value={formatPkr(stats.monthToDateSales)}
          delta="Month to date"
          tone="good"
        />
      </div>

      <div className="grid grid-cols-1 items-start gap-4 3xl:grid-cols-[1.35fr_1fr]">
        <section className="card mb-4 bg-bg p-5 shadow-sm">
          <div className="mb-4 flex items-baseline justify-between">
            <h2 className="m-0 text-[21px]">Sales · last 12 weeks</h2>
            {peak > 0 && <span className="tag tag-accent-2">Peak {formatPkr(peak)}</span>}
          </div>

          {/*  A table behind the bars: the chart is a picture of it, and a
              screen reader gets the numbers rather than a row of divs.     */}
          <table className="sr-only">
            <caption>Sales by week for the last twelve weeks</caption>
            <thead><tr><th>Week starting</th><th>Sales</th><th>Orders</th></tr></thead>
            <tbody>
              {salesChart.map(week => (
                <tr key={week.weekStart}>
                  <td>{formatShortDate(week.weekStart)}</td>
                  <td>{formatPkr(week.sales)}</td>
                  <td>{week.orderCount}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="flex h-[200px] items-end gap-2" aria-hidden="true">
            {salesChart.map((week, index) => (
              <div key={week.weekStart} className="flex h-full flex-1 flex-col items-center justify-end gap-1.5">
                <div
                  className="min-h-1 w-full rounded-b-[4px] rounded-t-xl"
                  style={{
                    height: `${Math.max((week.sales / peak) * 100, 2)}%`,
                    // The last four weeks in the accent, as the design has it —
                    // the recent run reads at a glance.
                    background: index >= salesChart.length - 4
                      ? 'var(--color-accent)'
                      : 'var(--color-accent-2-400)',
                  }}
                  title={`${formatShortDate(week.weekStart)} · ${formatPkr(week.sales)}`}
                />
                <div className="text-[10px] text-neutral-600">W{index + 1}</div>
              </div>
            ))}
          </div>
        </section>

        <section className="card mb-4 bg-bg p-5 shadow-sm">
          <h2 className="mb-2.5 text-[21px]">Best sellers</h2>

          {bestSellers.length === 0 && (
            <p className="text-sm text-neutral-600">Nothing sold yet.</p>
          )}

          {bestSellers.map(product => (
            <div
              key={product.productId}
              className="flex items-center gap-3 border-b border-divider py-2 last:border-b-0"
            >
              <div
                className="washed h-10 w-10 flex-none rounded-[13px]"
                aria-hidden="true"
                style={{ background: fabricBackground(product.swatchColorValue, product.swatchWeave) }}
              />
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-semibold">
                  {product.name}
                </div>
                <div className="text-xs text-neutral-600">
                  {product.soldCount} sold
                </div>
              </div>
              <div className="whitespace-nowrap text-[13px] font-bold">
                {formatPkr(product.revenue)}
              </div>
            </div>
          ))}
        </section>
      </div>

      <section className="card mb-4 bg-bg p-5 shadow-sm">
        <div className="mb-2 flex items-baseline justify-between">
          <h2 className="m-0 text-[21px]">Latest orders</h2>
          <Link to="/shop-panel/orders" className="text-sm">All orders →</Link>
        </div>

        <div className="overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>Order</th><th>Customer</th><th>Items</th>
                <th>Total</th><th>Payment</th><th>Status</th>
              </tr>
            </thead>
            <tbody>
              {latestOrders.map(order => (
                <tr key={order.orderId}>
                  <td className="font-bold">
                    <Link to={`/shop-panel/orders/${order.orderId}`}>{order.reference}</Link>
                  </td>
                  <td>{order.customerName}</td>
                  <td className="max-w-[260px] truncate">
                    {order.itemSummary}
                  </td>
                  <td>{formatPkr(order.total)}</td>
                  <td>{order.paymentMethod.replace(/([A-Z])/g, ' $1').trim()}</td>
                  <td><StatusTag status={order.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

function StatTile({ label, value, delta, tone }: {
  label: string;
  value: string;
  delta: string;
  tone: 'good' | 'warn' | 'neutral';
}) {
  const color =
    tone === 'good' ? 'var(--color-accent-2-700)' :
    tone === 'warn' ? 'var(--color-accent-700)' :
    'var(--color-neutral-600)';

  return (
    <div className="card gap-1 bg-bg p-5 shadow-sm">
      <div className="card-kicker">{label}</div>
      <div className="font-heading text-3xl/[1.05]">{value}</div>
      <div className="text-xs" style={{ color }}>{delta}</div>
    </div>
  );
}
