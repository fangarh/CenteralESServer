create table if not exists processing_content_hashes (
    subject_id uuid not null references processing_subjects(id) on delete cascade,
    capability text not null,
    algorithm text not null,
    hash_value text not null,
    created_at timestamptz not null,
    primary key (subject_id, algorithm),
    unique (capability, hash_value)
);

create index if not exists ix_processing_content_hashes_lookup
    on processing_content_hashes (capability, hash_value);

insert into processing_content_hashes (
    subject_id,
    capability,
    algorithm,
    hash_value,
    created_at)
select
    id,
    capability,
    split_part(content_hash, ':', 1),
    content_hash,
    created_at
from processing_subjects
where position(':' in content_hash) > 0
on conflict do nothing;
