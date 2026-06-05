create table if not exists processor_endpoints (
    id uuid primary key,
    processor_key text not null,
    capability text not null,
    endpoint text not null,
    endpoint_normalized text not null,
    enabled boolean not null,
    concurrency_limit integer not null,
    priority integer not null default 0,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    disabled_at timestamptz null,
    constraint processor_endpoints_processor_key_not_blank check (btrim(processor_key) <> ''),
    constraint processor_endpoints_capability_not_blank check (btrim(capability) <> ''),
    constraint processor_endpoints_endpoint_not_blank check (btrim(endpoint) <> ''),
    constraint processor_endpoints_endpoint_normalized_not_blank check (btrim(endpoint_normalized) <> ''),
    constraint processor_endpoints_concurrency_limit_positive check (concurrency_limit > 0)
);

create unique index if not exists processor_endpoints_processor_capability_endpoint_normalized_uq
    on processor_endpoints (processor_key, capability, endpoint_normalized);

create index if not exists processor_endpoints_enabled_lookup_idx
    on processor_endpoints (processor_key, capability, enabled);
