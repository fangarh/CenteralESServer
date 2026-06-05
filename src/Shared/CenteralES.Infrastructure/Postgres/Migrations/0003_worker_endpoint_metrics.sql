create table if not exists processing_worker_endpoint_metrics (
    worker_id text not null references processing_worker_heartbeats(worker_id) on delete cascade,
    processor_key text not null,
    capability text not null,
    endpoint text not null,
    enabled boolean not null,
    health text not null,
    in_flight integer not null,
    concurrency_limit integer not null,
    heartbeat_at timestamptz not null,
    updated_at timestamptz not null,
    primary key (worker_id, endpoint)
);

create index if not exists ix_processing_worker_endpoint_metrics_processor
    on processing_worker_endpoint_metrics (processor_key, capability, heartbeat_at desc);
