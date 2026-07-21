# Marketplace CMS

Çok kiracılı (multi-tenant) pazaryeri CMS platformu. Merchant'lar sipariş, ürün, stok, fiyat ve kategori yönetimi yapar; pazaryeri sahibi raporlama ve platform yönetimi yürütür. Uçta bir mobil uygulamaya ve iki admin paneline (merchant + owner) servis verir.

- **İlk hedef entegrasyonlar:** Shopify (çift yönlü senkron), iyzico & PayPal (merchant başına ödeme sağlayıcı).
- **Backend:** .NET 10, mikroservis mimarisi, PostgreSQL, RabbitMQ, Keycloak.
- **Dağıtım:** Docker + Kubernetes.

> Ön yüz ekranları bu repo kapsamında değildir; backend REST API'leri sağlar.

## Mimari (özet)

Katmanlı mikroservis mimarisi — detay için [docs/architecture.md](docs/architecture.md).

```
Mobil / Merchant Paneli / Owner Paneli
              │
       API Gateway (YARP) + BFF
              │
 ┌────────────┼───────────────────────────────┐
 Identity  Merchant  Catalog  Inventory  Order
 Payment   Shopify-Sync  Notification  Reporting  Media
              │
   Event Bus (RabbitMQ + MassTransit, Outbox)
              │
 PostgreSQL(db-per-service) · Redis · Elasticsearch · MinIO
              │
        Shopify · iyzico · PayPal
```

### Mimari kararlar
| Konu | Karar |
|---|---|
| Shopify senkron | Çift yönlü (idempotency + çakışma çözümü) |
| Kimlik | Keycloak (OIDC/JWT) |
| Mesajlaşma | RabbitMQ + MassTransit (Outbox) |
| Çok kiracılılık | Ortak DB + `TenantId` (EF Core global query filter) |
| Veri | Database-per-service (PostgreSQL) |

## Repo yapısı

```
src/
  BuildingBlocks/Marketplace.BuildingBlocks   # Result, ITenantContext, Outbox, Secret şifreleme, ortak Web (auth/tenant)
  Contracts/Marketplace.Contracts             # Servisler arası paylaşılan integration event tipleri
  Gateway/Marketplace.Gateway                 # YARP reverse proxy + auth + rate limit
  Services/Catalog/Marketplace.Catalog.Api    # Ürün/kategori (outbox'lu)
  Services/Merchant/Marketplace.Merchant.Api  # Merchant onboarding + şifreli Shopify/ödeme config (outbox'lu)
  Services/Inventory/Marketplace.Inventory.Api # Stok + event consumer'ları (ProductCreated/OrderPlaced)
  Services/Order/Marketplace.Order.Api        # Sipariş + stok rezervasyon koreografisi
  Services/ShopifySync/Marketplace.ShopifySync.Api # Shopify çift yönlü senkron (IShopifyClient: simulator/graphql)
infra/keycloak/marketplace-realm.json         # Keycloak realm import
infra/postgres/init/                          # db-per-service oluşturma scriptleri
docs/architecture.md                          # Detaylı mimari + yol haritası
docker-compose.yml                            # Lokal ortam
```

## Ön koşullar

- .NET 10 SDK
- Docker + Docker Compose (bu makinede `colima` ile headless kuruludur)

## Hızlı başlangıç

```bash
cp .env.example .env
docker compose up -d --build
```

Servisler:
| Servis | Adres |
|---|---|
| Gateway | http://localhost:8081 |
| Catalog (doğrudan) | http://localhost:8082 |
| Merchant (doğrudan) | http://localhost:8083 |
| Inventory (doğrudan) | http://localhost:8084 |
| Order (doğrudan) | http://localhost:8085 |
| ShopifySync (doğrudan) | http://localhost:8086 |
| Keycloak | http://localhost:8080 (admin/admin) |
| RabbitMQ yönetim | http://localhost:15672 (guest/guest) |
| PostgreSQL | localhost:5432 |

### Swagger / API arayüzleri (dev)

Her API servisi kendi portunda interaktif Swagger UI sunar. **Authorize** düğmesine Keycloak JWT'sini yapıştırıp korumalı endpoint'leri deneyebilirsin (token için aşağıdaki `curl` bloğuna bak).

| Servis | Swagger UI | OpenAPI JSON |
|---|---|---|
| Catalog | http://localhost:8082/swagger | http://localhost:8082/openapi/v1.json |
| Merchant | http://localhost:8083/swagger | http://localhost:8083/openapi/v1.json |
| Inventory | http://localhost:8084/swagger | http://localhost:8084/openapi/v1.json |
| Order | http://localhost:8085/swagger | http://localhost:8085/openapi/v1.json |
| ShopifySync | http://localhost:8086/swagger | http://localhost:8086/openapi/v1.json |

Sağlık kontrolü:
```bash
curl http://localhost:8081/health        # gateway
curl http://localhost:8082/health        # catalog
```

Token alıp Catalog'a istek (demo merchant):
```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/marketplace/protocol/openid-connect/token \
  -d grant_type=password -d client_id=marketplace-mobile \
  -d username=demo-merchant -d password=demo123 | jq -r .access_token)

curl -H "Authorization: Bearer $TOKEN" http://localhost:8081/api/products
```

Merchant akışı (owner merchant oluşturur, merchant Shopify anahtarlarını bağlar):
```bash
# owner token'ı için username=demo-owner
OWNER=$(curl -s -X POST http://localhost:8080/realms/marketplace/protocol/openid-connect/token \
  -d grant_type=password -d client_id=marketplace-mobile \
  -d username=demo-owner -d password=demo123 | jq -r .access_token)

# owner: merchant oluştur (Id, Keycloak kullanıcısının tenant_id'sine eşlenir)
curl -X POST http://localhost:8081/api/merchants -H "Authorization: Bearer $OWNER" \
  -H "Content-Type: application/json" \
  -d '{"id":"11111111-1111-1111-1111-111111111111","name":"Demo Shop","commissionRate":0.10}'

# merchant: Shopify anahtarlarını bağla (at-rest AES-GCM ile şifrelenir)
curl -X PUT http://localhost:8081/api/merchants/me/integrations/shopify \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"shopDomain":"demo-shop.myshopify.com","accessToken":"shpat_..."}'
```

## Geliştirme (lokal, konteynersiz)

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build MarketplaceCms.slnx
# altyapıyı ayağa kaldır, uygulamayı lokal çalıştır:
docker compose up -d postgres rabbitmq keycloak
dotnet run --project src/Services/Catalog/Marketplace.Catalog.Api
```

### Migration üretme
```bash
dotnet ef migrations add <Ad> \
  --project src/Services/Catalog/Marketplace.Catalog.Api \
  --output-dir Infrastructure/Migrations
```

## Yol haritası

Faz 0 (bu iskelet) → Faz 1 çekirdek domain → Faz 2 Shopify → Faz 3 ödeme → Faz 4 mobil BFF → Faz 5 raporlama/owner → Faz 6 K8s sertleştirme. Detay: [docs/architecture.md](docs/architecture.md).
