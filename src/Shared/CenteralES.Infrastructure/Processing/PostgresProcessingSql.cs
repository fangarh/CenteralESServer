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
        )
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
            job.attempt_number;
        """;
}
