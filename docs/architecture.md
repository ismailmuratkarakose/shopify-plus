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
| **2** ✅ | Shopify | ShopifySync servisi, IShopifyClient (simulator/GraphQL), entegrasyon read-model, outbound ürün senkronu + eşleme + döngü önleme, inbound webhook (HMAC-SHA256 + idempotency + shop→tenant), Catalog ürün upsert (çakışma çözümü) + Inventory stok, çift yönlü doğrulandı |
| **3** ✅ | Ödeme | Payment servisi, `IPaymentProvider` (simulator + iyzico + PayPal gerçek-yapı), provider resolver (merchant config), Order↔Payment↔Inventory saga + telafi (ödeme başarısızsa stok geri bırakılır) |
| **4** ✅ | Mobil BFF | `Marketplace.Bff.Mobile.Api` (aggregation, DB'siz): Catalog+Inventory birleşik katalog DTO'su, Redis-backed sepet (kullanıcı+tenant kapsamlı), checkout → Order oluşturma → saga, sipariş takibi; downstream'e JWT forwarding (`AuthForwardingHandler`) |
| **5** ✅ | Raporlama + owner | `Marketplace.Reporting.Api` (event-sourced read-model, DB'siz publish): MerchantRegistered/OrderPlaced/PaymentSucceeded/PaymentFailed tüketir; komisyon = tutar × merchant oranı; scope-aware `/api/reports/*` (özet, owner-only merchant kırılımı, günlük ciro serisi) |
| **7** ✅ | **Katalog pivotu** | Global ürün master (Barkod/GTIN) + merchant teklif/offer modeli; barkodla find-or-create; Order satırı satıcıya (tenant) bağlı, alıcı BuyerRef (JWT sub); çok-merchant sepette checkout merchant başına N siparişe bölünür; BFF katalog = ürün→satıcı kıyası. Doğrulandı: aynı barkod-2 satıcı-farklı fiyat → 2 sipariş → farklı komisyon |
| **8** 🔜 | Kargo/Shipping | `IShippingProvider` (simulator + gerçek-yapı), merchant başına şifreli kargo config, Paid→gönderi oluştur + takip no + durum |
| **6** ⏸ | K8s sertleştirme | Helm chart'lar, autoscaling, probe'lar, observability, yük testi, compose startup yarışı fix |

> **Not (2026-07-22):** Katalog Faz 1-5'te "her merchant kendi bağımsız ürünü" olarak kuruldu. Gerçek pazaryeri gereksinimi (aynı ürün — barkod ile tek kimlik — birden çok satıcı, farklı fiyat) için Faz 7'de **ortak ürün master + teklif** modeline geçilecek. Frontend bu repoda yok (yalnızca REST API).

## 9. Uygulanan altyapı (Faz 1)

- **Transactional Outbox (özel):** `BuildingBlocks/Outbox` — `OutboxMessage` + `OutboxDispatcher<TContext>`. Event, iş verisiyle aynı `SaveChanges`'te yazılır; arka plan dispatcher RabbitMQ'ya güvenilir teslim eder (en-az-bir-kez). Catalog ve Merchant kullanıyor.
- **Ortak Web yapı taşları:** `BuildingBlocks/Web` — `AddKeycloakJwtAuth`, `UseTenantResolution`, `ResultHttpExtensions`. Tüm servislerde tek kaynaktan.
- **Secret şifreleme:** `AesGcmSecretProtector` (AES-256-GCM). Merchant ödeme/Shopify anahtarları at-rest şifreli. Anahtar K8s secret'tan (`Secrets__EncryptionKey`).
- **Servisler arası koreografi (Order↔Inventory):** `Marketplace.Contracts` paylaşılan event tipleri (MassTransit MessageUrn eşleşmesi). Akış: Catalog `ProductCreated` → Inventory stok kaydı açar; Order `OrderPlaced` → Inventory stok rezerve eder → `StockReserved`/`StockReservationFailed` → Order siparişi `Confirmed`/`Rejected` yapar. Consumer'lar HTTP dışında çalıştığından tenant'ı event'in `TenantId`'sinden kurar (`ITenantContext.SetTenant`). Tüm yayınlar outbox üzerinden (en-az-bir-kez; consumer'lar idempotent).
- **MassTransit kuyruk adlandırma:** Birden fazla servis aynı consumer tip adını kullanırsa (ör. Inventory ve ShopifySync'te `ProductCreatedConsumer`), varsayılan formatter aynı kuyruk adını üretir → tek kuyrukta yarışırlar (fan-out bozulur). Çözüm: her serviste `SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("<servis>", false))` ile servise özel önek → ayrı kuyruklar, her ikisi de exchange'e bağlı.
- **Shopify senkronu (ShopifySync):** Merchant `MerchantIntegrationConfigured` → ShopifySync credential'ı Merchant **internal endpoint**'inden (`X-Internal-Api-Key`, gateway'e route edilmez) çeker, token'ı shared key ile at-rest şifreli saklar. Secret event bus'a hiç düşmez.
  - **Outbound (Pazaryeri→Shopify):** `ProductCreated` → `IShopifyClient` (config `Shopify:ClientMode`: simulator/graphql) → push + `ProductMapping` (Sku anahtarlı). Döngü önleme: `Source=shopify` olan ürün tekrar push edilmez.
  - **Inbound (Shopify→Pazaryeri):** webhook endpoint'leri (`/webhooks/shopify/*`, anonim, HMAC-SHA256 ile korunur). Pipeline: raw body HMAC doğrula → `WebhookInbox` ile idempotency (`X-Shopify-Webhook-Id`) → `X-Shopify-Shop-Domain` ile shop→tenant çöz → payload parse → integration event outbox'a. Catalog `ProductUpsertedFromShopify` consumer'ı ürünü Sku ile upsert eder (`Source=shopify`, çakışma: pazaryeri sürümü daha yeniyse atla). Inventory `StockChangedFromShopify` consumer'ı stoğu uygular.
  - **Webhook sıra bağımsızlığı:** stok, ürün/envanter kaydından önce gelebilir → Inventory consumer'ı kayıt yoksa exception atar, `UseMessageRetry` kademeli retry ile kayıt oluşunca uygular.
- **Ödeme saga (Faz 3, Order↔Payment↔Inventory):** Sipariş → `OrderPlaced` → stok rezerve → `StockReserved` → Order `AwaitingPayment` + `PaymentRequested` → Payment sağlayıcıyı çözer (`IPaymentProvider`: simulator/iyzico/paypal, config `Payment:Mode` + merchant config) → tahsilat → `PaymentSucceeded`/`PaymentFailed`. Order: başarı → `Paid`; başarısızlık → `PaymentFailed` + `StockReleaseRequested` (**telafi**) → Inventory rezerve stoğu geri bırakır. Sağlayıcı credential'ları Merchant internal endpoint'inden çözülür; simülatörde `Payment:Simulator:FailAmount` ile başarısızlık test edilir. Not: dış tahsilat + DB commit tek adımda; prod'da ödeme idempotency-key + iki-aşamalı kayıt önerilir.
- **Mobil BFF (Faz 4, Backend-for-Frontend):** `Marketplace.Bff.Mobile.Api` müşteri uygulamasının ekranlarına göre sadeleştirilmiş uçlar sunar (`/api/mobile/*`). Kendi veritabanı yoktur; downstream servisleri (Catalog, Inventory, Order) **typed HttpClient** ile çağırır ve tek DTO'da birleştirir (ör. katalog = ürün + `available`/`inStock`). **Sepet** Redis'te kullanıcı+tenant kapsamlı anahtarda (`cart:{tenant}:{sub}`, TTL 7g) tutulur; satırlar fiyat/başlık anlık kopyası taşır. **Checkout** sepeti `POST /api/orders`'a çevirir (saga tetiklenir) ve sepeti temizler. BFF ayrı servis hesabı kullanmaz: gelen kullanıcı JWT'sini bir `DelegatingHandler` (`AuthForwardingHandler`) ile downstream'e taşır; böylece kimlik/tenant zincir boyunca korunur. Stok kontrolü BFF'te bilgi amaçlıdır (409); nihai rezervasyon Order↔Inventory saga'sında yapılır.
- **Raporlama (Faz 5, event-sourced read-model):** `Marketplace.Reporting.Api` domain event'lerini tüketip raporlama read-model'i (kendi `reporting` DB'si) oluşturur; event yayınlamaz → outbox yok. `OrderPlaced` → `SalesFact(Pending)` (tutar burada bilinir), `PaymentSucceeded` → `Paid` + komisyon = `tutar × oran` (oran, `MerchantRegistered`'dan beslenen `MerchantRate` read-model'inden anlık kopyalanır), `PaymentFailed` → `Failed` (ciro dışı). Event sırası garanti olmadığından (ör. ödeme, sipariş kaydından önce gelebilir) consumer kayıt yoksa exception atar → `UseMessageRetry`. `PaymentSucceeded` tutar taşımadığından `OrderPlaced`'ın tutarı SalesFact'te saklanır (event join, OrderId anahtarıyla idempotent). Uçlar scope-aware: owner/platform tüm tenant'ları, merchant kendi verisini görür; merchant kırılımı yalnızca owner (403). **Cold-start uyarısı:** Reporting devreye girmeden önce kaydolmuş merchant'ların `MerchantRegistered`'ı kaçırılır (oran bilinmez) — prod'da event replay / snapshot beslemesi gerekir. Ayrıca taze `.data` açılışında koşulsuz `depends_on` olan servisler postgres init'ini beklemeden migrate deneyip çökebilir; yeniden başlatma çözer (kalıcı çözüm Faz 6: healthcheck koşulları + init container).
- **Katalog pivotu (Faz 7, ortak ürün + teklif):** Katalog iki katmanlı: **`Product` master** (GLOBAL, Barkod/GTIN ile benzersiz, tenant'sız) + **`Offer`** (tenant-owned, master'a fiyat/sku bağlar; merchant başına master için tek). Aynı barkod → tek master → çok satıcı, farklı fiyat. Teklif oluşturma barkodla **find-or-create** (ilk satıcı master'ı doğurur). Ürün/satıcı görünümleri `IgnoreQueryFilters` ile tüm satıcıları gösterir; merchant yönetim uçları (`/api/offers`) tenant-filtered. Stok Inventory'de (tenant, master) anahtarlı — offer event'i (`ProductCreated`, geriye-uyum için isim korundu; Barcode+OfferId eklendi) ile açılır. **Alıcı≠satıcı:** sipariş **satıcı merchant'a** aittir (`Order.TenantId`=satıcı), alıcı `BuyerRef`=JWT sub; merchant `/api/orders`'da satışlarını, alıcı `/api/orders/purchases`'da alımlarını görür. **Checkout bölme:** BFF sepeti satıcıya göre gruplar → merchant başına bir `POST /api/orders` (MerchantId=satıcı) → her sipariş kendi saga'sını (stok/ödeme/komisyon) yürütür. Kategori global taksonomi oldu. Not: BFF'te ekleme-anı stok kontrolü kaldırıldı (cross-tenant stok gerekir); kullanılabilirlik checkout saga'sında zorlanır.

## 10. Açık teknik notlar / kararlar

- **MassTransit v9+ ticari** → 8.5.10'a sabitlendi. EF-outbox alt paketi net10 uyumsuz olduğundan özel outbox yazıldı.
- **MediatR 14.x** ticari lisans gerektirebilir; gözden geçirilecek (gerekirse ücretsiz alternatif).
- **Keycloak `tenant_id` mapper:** realm import'unda tanımlı; prod realm'i IaC ile yönetilecek. Audience mapper eklenip `ValidateAudience=true` yapılacak.
- **Central Package Management:** servis sayısı artınca `Directory.Packages.props`'a geçilecek.
- **Migration:** dev'de startup'ta otomatik; prod'da ayrı migration job'ı.
