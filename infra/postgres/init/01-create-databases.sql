-- Database-per-service: her mikroservis kendi DB'sine sahip (aynı Postgres sunucusunda, dev için).
-- Not: bu script yalnızca postgres data dizini İLK kez oluşturulurken çalışır.
-- Mevcut volume'de yeniden çalıştırmak için: rm -rf ./.data
CREATE DATABASE merchant;
CREATE DATABASE shopifysync;
