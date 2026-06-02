namespace CenteralES.Infrastructure.Processing;

public static class PostgresProcessingSql
{
    public const string ClaimNext = """
        with candidate as (
            select id
            from processing_jobs
            where status = 'queued'
              and scheduled_at <= @now
            order by scheduled_at, created_at
            for update skip locked
            limit 1
        ),
        updated_job as (
        update processing_jobs job
        set status = 'processing',
            started_at = @now,
            heartbeat_at = @now,
            updated_at = @now
        from candidate
        where job.id = candidate.id
        returning
            job.id,
            job.subject_id,
            job.capability,
            job.content_hash,
            job.temporary_file_key,
            job.attempt_number
        )
        update processing_subjects subject
        set state = 'processing',
            current_job_id = updated_job.id,
            updated_at = @now
        from updated_job
        where subject.id = updated_job.subject_id
        returning
            updated_job.id,
            updated_job.subject_id,
            updated_job.capability,
            updated_job.content_hash,
            updated_job.temporary_file_key,
            updated_job.attempt_number;
        """;

    public const string RecoverStaleProcessingJobs = """
        with candidate as (
            select job.id, job.subject_id
            from processing_jobs job
            join processing_subjects subject on subject.id = job.subject_id
            where job.status = 'processing'
              and job.capability = @capability
              and subject.current_job_id = job.id
              and subject.state = 'processing'
              and coalesce(job.heartbeat_at, job.started_at, job.updated_at) < @stale_before
            order by coalesce(job.heartbeat_at, job.started_at, job.updated_at), job.started_at, job.created_at
            for update skip locked
            limit @limit
        ),
        updated_jobs as (
            update processing_jobs job
            set status = 'queued',
                scheduled_at = @recovered_at,
                started_at = null,
                heartbeat_at = null,
                updated_at = @recovered_at
            from candidate
            where job.id = candidate.id
            returning job.id, job.subject_id
        ),
        updated_subjects as (
            update processing_subjects subject
            set state = 'queued',
                updated_at = @recovered_at
            from updated_jobs
            where subject.id = updated_jobs.subject_id
              and subject.current_job_id = updated_jobs.id
            returning subject.id
        )
        select count(*)::int
        from updated_subjects;
        """;
}
