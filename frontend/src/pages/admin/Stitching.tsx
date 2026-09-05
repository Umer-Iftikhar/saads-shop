import { ErrorState, Loading } from '../../components/Feedback';
import { fabricBackground } from '../../lib/fabric';
import { formatShortDate } from '../../lib/format';
import { useStitchingBoard, useUpdateStitchingJob } from '../../hooks/adminQueries';

/**
 * Frame 12 — the stitching queue.
 *
 * Four columns: Measuring, Cutting, Stitching, Ready. The grouping and the
 * column order come from the API so an empty column still appears — a floor
 * with nothing at the cutting table should show an empty Cutting column, not
 * silently skip it and renumber the board.
 *
 * Jobs move with buttons rather than drag-and-drop: drag is unusable by
 * keyboard, fiddly on the phone the floor actually uses, and this is a
 * four-step pipeline where "next" is unambiguous.
 */
const NEXT_STAGE: Record<string, string> = {
  Measuring: 'Cutting',
  Cutting:   'Stitching',
  Stitching: 'Ready',
  Ready:     'Done',
};

export function Stitching() {
  const board = useStitchingBoard();
  const update = useUpdateStitchingJob();

  if (board.isPending) return <Loading label="Reading the floor…" />;
  if (board.isError || !board.data) {
    return <ErrorState title="Could not load the stitching queue" onRetry={() => board.refetch()} />;
  }

  const total = board.data.columns.reduce((sum, c) => sum + c.count, 0);

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Stitching queue</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {total === 0 ? 'Nothing on the floor right now' : `${total} ${total === 1 ? 'job' : 'jobs'} on the floor`}
          </div>
        </div>
      </header>

      <div className="grid grid-cols-1 items-start gap-3.5 sm:grid-cols-2 3xl:grid-cols-4">
        {board.data.columns.map(column => (
          <section key={column.stage} className="rounded-lg bg-bg p-4 shadow-sm">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="m-0 font-heading text-[17px]">{column.stage}</h2>
              <span className="tag tag-neutral">{column.count}</span>
            </div>

            {column.jobs.length === 0 && (
              <p className="m-0 text-[13px] text-neutral-600">Empty.</p>
            )}

            {column.jobs.map(job => (
              <div key={job.stitchingJobId} className="mb-2.5 block rounded-md bg-neutral-200 p-3 text-[13px]">
                <div className="mb-2 flex items-center gap-2">
                  <div
                    className="washed h-7 w-7 flex-none rounded-[10px]" aria-hidden="true"
                    style={{ background: fabricBackground(job.swatchColorValue, job.swatchWeave) }}
                  />
                  <span className="text-[13px] font-bold">{job.reference}</span>
                  {job.isOverdue && <span className="tag tag-accent ml-auto">Late</span>}
                </div>

                <div className="text-[13px]">{job.title}</div>

                <div className="mt-1 text-xs text-neutral-600">
                  {job.assignedTo ?? 'Unassigned'}
                  {job.dueDate ? ` · due ${formatShortDate(job.dueDate)}` : ''}
                </div>

                {NEXT_STAGE[job.stage] && (
                  <button
                    type="button"
                    className="btn btn-secondary mt-2.5 text-[13px]"
                    disabled={update.isPending}
                    onClick={() => update.mutate({ jobId: job.stitchingJobId, stage: NEXT_STAGE[job.stage] })}
                    aria-label={`Move ${job.reference} to ${NEXT_STAGE[job.stage]}`}
                  >
                    Move to {NEXT_STAGE[job.stage]} →
                  </button>
                )}
              </div>
            ))}
          </section>
        ))}
      </div>
    </>
  );
}
