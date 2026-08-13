-- Phase 4 — PostgreSQL topology: modül başına schema (ADR-0001, ADR-0009).
-- Bu script yalnızca container İLK oluşturulduğunda çalışır
-- (docker-entrypoint-initdb.d; data volume boşken). Mevcut volume'de tekrar çalışmaz.
-- Modül sahipliği: her modül kendi şemasının sahibidir; çapraz modül FK yasaktır.

CREATE SCHEMA IF NOT EXISTS master_data;
CREATE SCHEMA IF NOT EXISTS facility;
CREATE SCHEMA IF NOT EXISTS inventory;
CREATE SCHEMA IF NOT EXISTS inbound;
CREATE SCHEMA IF NOT EXISTS outbound;
CREATE SCHEMA IF NOT EXISTS transfers;
CREATE SCHEMA IF NOT EXISTS fulfillment;
CREATE SCHEMA IF NOT EXISTS administration;
