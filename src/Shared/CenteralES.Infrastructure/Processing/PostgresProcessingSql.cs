namespace CenteralES.Infrastructure.Processing;

public static class PostgresProcessingSql
{
    public const string Schema = """
        create table if not exists processing_subjects (
            id uuid primary key,
            capability text not null,
            content_hash text not null,
            current_job_id uuid null,
            state text not null,
            result_id uuid null,
            created_at timestamptz not null,
            updated_at timestamptz not null,
            unique (capability, content_hash)
        );

        create table if not exists processing_jobs (
            id uuid primary key,
            subject_id uuid not null references processing_subjects(id),
            capability text not null,
            content_hash text not null,
            temporary_file_key text not null,
            attempt_number integer not null,
            status text not null,
            scheduled_at timestamptz not null,
            started_at timestamptz null,
            finished_at timestamptz null,
            heartbeat_at timestamptz null,
            created_at timestamptz not null,
            updated_at timestamptz not null
        );

        create index if not exists ix_processing_jobs_claim
            on processing_jobs (status, scheduled_at, created_at);

        create table if not exists processing_attempt_diagnostics (
            job_id uuid primary key references processing_jobs(id),
            endpoint text null,
            duration_ms integer null,
            http_status integer null,
            normalized_error_code text null,
            retryable boolean null,
            raw_error_excerpt text null,
            correlation_id text not null,
            created_at timestamptz not null
        );

        create table if not exists processing_result_index (
            id uuid primary key,
            subject_id uuid not null references processing_subjects(id),
            capability text not null,
            content_hash text not null,
            job_id uuid not null references processing_jobs(id),
            result_kind text not null,
            payload_table text not null,
            payload_id uuid not null,
            contract_version text not null,
            payload_size bigint not null,
            created_at timestamptz not null,
            unique (capability, content_hash)
        );

        create table if not exists pdf_stamp_recognition_results (
            id uuid primary key,
            result_index_id uuid not null references processing_result_index(id),
            payload_json jsonb not null,
            contract_version text not null,
            created_at timestamptz not null
        );

        create table if not exists processing_worker_heartbeats (
            worker_id text primary key,
            processor_key text not null,
            capability text not null,
            started_at timestamptz not null,
            heartbeat_at timestamptz not null,
            updated_at timestamptz not null
        );

        create index if not exists ix_processing_worker_heartbeats_processor
            on processing_worker_heartbeats (processor_key, capability, heartbeat_at desc);

        create table if not exists client_applications (
            key_id text primary key,
            name text not null,
            secret_hash text not null,
            is_active boolean not null,
            allowed_capabilities text[] not null,
            created_at timestamptz not null,
            updated_at timestamptz not null,
            expires_at timestamptz null,
            last_used_at timestamptz null,
            last_used_ip text null,
            last_used_user_agent text null,
            disabled_at timestamptz null
        );
        """;

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
