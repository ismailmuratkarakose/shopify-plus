-- Database-per-service: her mikroservis kendi DB'sine sahip (aynı Postgres sunucusunda, dev için).
-- Not: bu script yalnızca postgres data dizini İLK kez oluşturulurken çalışır.
-- Mevcut volume'de yeniden çalıştırmak için: docker compose down -v
CREATE DATABASE merchant;
CREATE DATABASE inventory;
CREATE DATABASE ordering;
CREATE DATABASE shopifysync;
