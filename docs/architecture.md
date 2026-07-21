# Mimari & Yol Haritası

## 1. Amaç ve aktörler

Çok kiracılı pazaryeri platformu. Üç aktör:

- **Mobil uygulama kullanıcısı (müşteri):** ürün gezme, sepet, sipariş, ödeme.
- **Merchant:** ürün, kategori, stok, fiyat, sipariş yönetimi; Shopify ve ödeme sağlayıcı (iyzico/PayPal) bağlama.
- **Pazaryeri sahibi (owner/platform-admin):** raporlama, komisyon, merchant onboarding, platform geneli gözetim.

## 2. Mimari ilkeler

- **Database-per-service:** her servis kendi PostgreSQL şemasına/DB'sine sahip. Servisler birbirinin tablosuna dokunmaz.
- **Asenkron öncelikli:** servisler arası iletişim RabbitMQ + MassTransit üzerinden event ile. Senkron çağrı minimumda.
- **Outbox pattern:** DB transaction'ı ile event yayınını atomik yapmak için (özellikle ödeme ve Shopify).
- **Idempotency:** Shopify webhook'ları ve ödeme callback'leri tekrarlanabilir; her tüketici idempotent.
- **Ortak DB + TenantId:** merchant izolasyonu EF Core global query filter ile. Her yazımda `TenantId` otomatik atanır.

## 3. Servisler ve sınırlar

| Servis | Sorumluluk |
|---|---|
| **Identity** | Keycloak entegrasyonu, tenant çözümleme (JWT `tenant_id` claim'i), rol/yetki |
| **Merchant** | Merchant onboarding, profil, ödeme sağlayıcı konfigürasyonu, komisyon oranları |
| **Catalog** | Ürün, kategori, varyant. Arama için Elasticsearch'e event ile beslenir |
| **Inventory** | Stok ve fiyat. Yüksek yazma trafiği; ayrı ölçeklenir |
| **Order** | Sepet, sipariş, saga orkestrasyonu (ödeme + stok + Shopify koordinasyonu) |
| **Payment** | `IPaymentProvider` soyutlaması; iyzico & PayPal implementasyonları |
| **Shopify Sync** | Çift yönlü Shopify entegrasyonu: webhook alıcı + outbound push worker |
| **Notification** | Push, e-posta, SMS |
| **Reporting** | Owner paneli için ciro, komisyon, merchant performansı |
| **Media** | Ürün görselleri ve dosyalar (MinIO/S3) |

## 4. Teknoloji yığını

- **.NET 10**, ASP.NET Core minimal API, her serviste CQRS + MediatR + FluentValidation.
- **EF Core + Npgsql** (PostgreSQL), **Redis** (cache/session/idempotency), **Elasticsearch** (arama), **MinIO/S3** (medya).
- **YARP** gateway + mobil **BFF**.
- **MassTransit + RabbitMQ** (Outbox), **Keycloak** (OIDC/JWT).
- **Serilog + OpenTelemetry** → Prometheus/Grafana/Loki.
- **Docker** multi-stage + **Helm** (K8s).

## 5. Çok kiracılılık (multi-tenancy)

- JWT içindeki `tenant_id` claim'i `TenantResolutionMiddleware` ile `ITenantContext`'e yazılır.
- `owner` / `platform-admin` rolleri **platform kapsamı** kazanır → tüm tenant'ları görebilir.
- `CatalogDbContext` global query filter: `IsPlatformScope || TenantId == currentTenant`.
- Yeni kayıtlarda `TenantId` `SaveChangesAsync` içinde otomatik atanır.

> Güvenlik notu: tenant sızıntısı en kritik risk. Her serviste global query filter + entegrasyon testleri zorunlu.

## 6. Shopify çift yönlü senkron (Faz 2)

- **Kaynak-önceliği + `updatedAt`** ile çakışma çözümü (kör last-write-wins değil).
- `Product.Source` (`marketplace`/`shopify`) ve `LastSyncedAt` alanları senkron kararında kullanılır.
- Inbound: Shopify webhook → idempotent handler → domain event.
- Outbound: domain event → Outbox → Shopify Admin API push worker.

## 7. Ödeme (Faz 3)

- `IPaymentProvider` arayüzü: `CreatePayment`, `Capture`, `Refund`, `HandleWebhook`.
- Merchant başına sağlayıcı konfigürasyonu Merchant servisinde **şifreli** saklanır.
- Order ↔ Payment saga; ödeme callback'leri idempotent.

## 8. Yol haritası (fazlar)

| Faz | Kapsam | Çıktı |
|---|---|---|
| **0** ✅ | İskele | Monorepo, BuildingBlocks, Gateway, Catalog dikey kesiti, docker-compose, Keycloak realm |
| **1** ✅ | Çekirdek domain | Merchant onboarding + şifreli entegrasyon config, Outbox, ortak Web yapı taşları, `Marketplace.Contracts`, Inventory + Order + event-driven koreografi (sipariş↔stok rezervasyonu) |
| **2** | Shopify | Çift yönlü senkron, webhook, çakışma çözümü |
| **3** | Ödeme | iyzico + PayPal, saga, merchant config |
| **4** | Mobil BFF | Katalog listeleme, sepet, checkout, sipariş takibi |
| **5** | Raporlama + owner | Komisyon, ciro, dashboard API'leri |
| **6** | K8s sertleştirme | Helm chart'lar, autoscaling, probe'lar, observability, yük testi |

## 9. Uygulanan altyapı (Faz 1)

- **Transactional Outbox (özel):** `BuildingBlocks/Outbox` — `OutboxMessage` + `OutboxDispatcher<TContext>`. Event, iş verisiyle aynı `SaveChanges`'te yazılır; arka plan dispatcher RabbitMQ'ya güvenilir teslim eder (en-az-bir-kez). Catalog ve Merchant kullanıyor.
- **Ortak Web yapı taşları:** `BuildingBlocks/Web` — `AddKeycloakJwtAuth`, `UseTenantResolution`, `ResultHttpExtensions`. Tüm servislerde tek kaynaktan.
- **Secret şifreleme:** `AesGcmSecretProtector` (AES-256-GCM). Merchant ödeme/Shopify anahtarları at-rest şifreli. Anahtar K8s secret'tan (`Secrets__EncryptionKey`).
- **Servisler arası koreografi (Order↔Inventory):** `Marketplace.Contracts` paylaşılan event tipleri (MassTransit MessageUrn eşleşmesi). Akış: Catalog `ProductCreated` → Inventory stok kaydı açar; Order `OrderPlaced` → Inventory stok rezerve eder → `StockReserved`/`StockReservationFailed` → Order siparişi `Confirmed`/`Rejected` yapar. Consumer'lar HTTP dışında çalıştığından tenant'ı event'in `TenantId`'sinden kurar (`ITenantContext.SetTenant`). Tüm yayınlar outbox üzerinden (en-az-bir-kez; consumer'lar idempotent).

## 10. Açık teknik notlar / kararlar

- **MassTransit v9+ ticari** → 8.5.10'a sabitlendi. EF-outbox alt paketi net10 uyumsuz olduğundan özel outbox yazıldı.
- **MediatR 14.x** ticari lisans gerektirebilir; gözden geçirilecek (gerekirse ücretsiz alternatif).
- **Keycloak `tenant_id` mapper:** realm import'unda tanımlı; prod realm'i IaC ile yönetilecek. Audience mapper eklenip `ValidateAudience=true` yapılacak.
- **Central Package Management:** servis sayısı artınca `Directory.Packages.props`'a geçilecek.
- **Migration:** dev'de startup'ta otomatik; prod'da ayrı migration job'ı.
