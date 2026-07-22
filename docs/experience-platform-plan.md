# Shopify Mobil Deneyim & Kişiselleştirme Platformu — Gap Analizi & Fazlı Yol Haritası

> Kaynak: müşteri dökümanı "SHOPIFY MOBİL DENEYİM VE KİŞİSELLEŞTİRME PLATFORMU" (12 sayfa).
> Karar (2026-07-22): mevcut kod tabanı bu spesifikasyona **dönüştürülecek**; önce zorunlu çekirdek
> (Faz A–L), opsiyonel modüller (Faz N) sonraya. **Mobil uygulama (React Native) ve web admin UI
> kapsam dışı**; yalnızca backend/REST API'ler (mobilin ve web panelin *backing* servisleri) inşa edilir.

## 1. Ürün şekli farkı (kritik)

Şu ana kadar **kendi ödeme/sipariş/komisyon akışı olan çok-satıcılı bir pazaryeri** kuruldu (Faz 0–5, 7).
Bu döküman ise farklı bir ürün tarif ediyor:

- **Checkout & ödeme Shopify'ındır.** ("Ödeme süreçleri Shopify Checkout üzerinden yürütülür";
  ödeme yöntemleri Shopify mağazasında aktif olanlara bağlı.) → Kendi Payment servisi / ödeme sağlayıcıları gereksiz.
- **Shopify kaynak sistemdir.** Ürün, koleksiyon, stok, fiyat, indirim, sipariş, müşteri, sayfa → Shopify'dan
  senkronlanır (read-model). → Kendi Order saga'sı, Inventory rezervasyonu, Catalog master/offer modeli uyumsuz.
- **Pazaryeri değildir.** Her müşteri = bir Shopify mağazası (tenant = mağaza). "Çoklu Mağaza" yalnızca opsiyonel modül.
- **Asıl ürün:** Deneyim Yönetim Platformu (CMS) + Kişiselleştirme/Gelir Optimizasyon Motoru + Shopify senkron;
  React Native mobil uygulamaya *dinamik config/deneyim* servis eder.

## 2. Mevcut varlıklar — gap analizi

| Bileşen (mevcut) | Bu spec'te durum | Aksiyon |
|---|---|---|
| Gateway (YARP), Keycloak (OIDC/JWT), RabbitMQ/MassTransit Outbox, EF Core, docker-compose, çok-kiracılık | ✅ Aynen | tenant = Shopify mağazası |
| BuildingBlocks (Result, ITenantContext, ISecretProtector, ortak Web/auth) | ✅ Aynen | — |
| Merchant servisi (hesap + AES-GCM şifreli Shopify config, internal credential endpoint) | ✅ Repurpose | "Store onboarding + Shopify bağlantısı (OAuth)" |
| ShopifySync (inbound/outbound, HMAC webhook, idempotency, shop→tenant, IShopifyClient) | ✅ Çekirdek — genişlet | tüm Shopify varlıkları için senkron |
| BFF (JWT forwarding, aggregation) | ✅ Repurpose | "Mobile Experience API" |
| Reporting (event-sourced read-model) | 🟡 Temel | Analitik/ölçümlemeye evril |
| `IPaymentProvider` + resolver **deseni** | ✅ Desen olarak | `IAiProvider` vb. için şablon |
| Catalog **master+offer** (Faz 7) | 🔴 Uyumsuz | Shopify ürün read-model'i ile değiştir |
| Inventory (kendi rezervasyon) | 🔴 Uyumsuz | Shopify stok read-model'i (rezervasyon yok) |
| Payment servisi + iyzico/PayPal (Faz 3) | 🔴 Gereksiz | arşivle (checkout Shopify'da) |
| Order **saga** + komisyon (Faz 3/5/7) | 🔴 Uyumsuz | Order → Shopify sipariş read-model'i (saga yok); komisyon kaldır |

## 3. Yeniden hizalama — SİLİNDİ (2026-07-22)

Pazaryeri-özel yapı **arşivlenmedi, tamamen silindi** (kullanıcı kararı):
- **Silinen servisler:** Catalog (master/offer), Inventory (rezervasyon), Order (saga), Payment (saga),
  Reporting (komisyon), BFF (pazaryeri checkout). → 6 servis, ~75 dosya.
- **Contracts** Store yaşam döngüsüne indirgendi (yalnızca `MerchantRegistered` + `MerchantIntegrationConfigured`).
- **ShopifySync:** outbound `ProductCreatedConsumer` silindi; webhook processor artık read-model'i günceller
  (event yayınlamıyor) — pull-sync'in incremental tamamlayıcısı.
- **compose/gateway/init/slnx/README** temizlendi. Kalan konteynerler: postgres, rabbitmq, keycloak,
  **merchant**, **shopifysync**, **gateway**.
- **Koru & yeniden yaz:** Mobile Experience API (BFF yerine) → Faz D; Analitik (Reporting yerine) → Faz K.

> Silinen kod git geçmişinde durur (commit'ler). Geri kazanım gerekirse geçmişten alınabilir.

## 4. Fazlı yol haritası (çekirdek A–L, opsiyonel N; UI hariç)

### Faz A — Yeniden hizalama & Shopify bağlantısı  🟡 (bağlantı ✅)
- Kavram: tenant = Shopify mağazası. Merchant→Store terminolojisi (kavramsal; toplu rename ertelendi).
- **Shopify OAuth akışı ✅ (2026-07-22):** `IShopifyOAuth` (simulator token üretir / graphql authorize+exchange);
  `POST /api/shopify/connect` (mağaza sahibi, JWT tenant) + `GET /shopify/oauth/callback` (anonim, graphql).
  Token Merchant'a **internal write** (`POST /internal/integrations/{id}/shopify`) ile kaydedilir →
  mevcut `MerchantIntegrationConfigured` pipeline'ı read-model'i senkronlar (secret bus'a düşmez).
  Doğrulandı (smokeA): connect → şifreli token → entegrasyon aktif (maskeli `****2bff`).
- **Ertelendi → Faz B:** pazaryeri-özel servislerin (Payment/Order-saga/master-offer/Inventory-rezervasyon)
  devre dışı/arşivi — yerlerine Shopify read-model geldiğinde yapılacak (doğrulanmış akış yarım kalmasın).
- **Bilinen kenar durum:** `CreateMerchant` yalnızca slug benzersizliğini kontrol ediyor; var olan Id ile
  farklı isim → PK çakışması (500 yerine 409 olmalı). Ayrı görev.

### Faz B — Shopify senkron genişletme (read-model)  🟡 (ürün+koleksiyon ✅)
- Varlıklar: **ürün, varyant, koleksiyon** ✅ | kalan: stok*, fiyat*, indirim, sipariş, müşteri, sayfa
  (*stok/fiyat varyantta zaten geliyor).
- **Dilim 1 ✅ (2026-07-22):** ShopifySync = "Store Data" servisi. `IShopifyClient`'a read (`GetProductsAsync`/
  `GetCollectionsAsync`, simulator deterministik katalog + graphql stub); read-model entity'leri
  (`SyncedProduct`/`SyncedVariant`/`SyncedCollection`, tenant=mağaza); `StoreSyncService` (credential çöz →
  Shopify'dan çek → full-refresh upsert); uçlar `POST /api/shopify/sync`, `GET /api/shopify/products`,
  `/products/{id}`, `/collections`. Migration: ShopifyReadModel. Doğrulandı (smokeB): sync → 5 ürün
  (varyant+sku+barkod+fiyat+stok) + 2 koleksiyon.
- **Kalan dilimler:** webhook→read-model incremental (mevcut inbox/HMAC deseni) + periyodik reconciliation;
  sipariş/müşteri/sayfa/indirim; rate-limit/backoff; ardından pazaryeri Catalog/Order/Inventory/Payment ARŞİVİ.
- Çıktı: mağaza verisinin güncel read-model'i; CMS ve kişiselleştirme bunu okur.

### Faz C — Deneyim/İçerik CMS çekirdeği (sürükle-bırak veri modeli)
- **Ekran/Sayfa** modeli: Home, PLP, PDP, Cart, Campaign, Landing.
- **Bileşen** modeli: Banner, Ürün gösterim, Koleksiyon, Kampanya, Popup, Kişiselleştirme, Dinamik içerik
  (sıra, konum, hedefleme, veri bağlama — Shopify koleksiyon/ürün referansları).
- **İçerik yaşam döngüsü:** Draft → Preview → Publish + **versiyonlama** (geçmiş, geri alma).
- Çıktı: web panelin çağıracağı CMS API'leri (UI ayrı ekip).

### Faz D — Remote Config + Mobile Experience API
- Mobilin **app-güncellemesiz** çektiği sürümlü config: sayfa düzenleri, banner, kampanya, tema,
  **feature flag** (özellik aç/kapa), deep link yönlendirmeleri, kişiselleştirme alanları.
- Publish'e bağlı yayın; preview kanalı (test cihazı); ETag/cache + CDN dostu.
- Çıktı: `GET /mobile/experience/{screen}` benzeri, sürümlü, hızlı config servis.

### Faz E — Banner / Popup / Kampanya yönetimi
- Banner: koleksiyon/sayfa/kampanya bazlı. Popup: sayfa/koleksiyon/kampanya bazlı + **zamanlanabilir**.
- Hedefleme kuralları (Faz G segment'leriyle entegre olabilir).

### Faz F — Tema yönetimi
- Renkler (ana/yardımcı), yazı tipleri, buton/kart/banner/navigasyon stilleri.
- App güncellemesi gerektirmeden Remote Config üzerinden yayın.

### Faz G — Kişiselleştirme & Gelir Optimizasyon Motoru
- **Davranış event ingestion** (görüntüleme, arama, sepet, satın alma — mobilden).
- Öneri senaryoları: ilgini çekebilecek, sana özel, benzer, favoriler, son gezilen, aramaya özel,
  popüler, sepete alternatif, tamamlayıcı, çapraz-satış.
- **Pluggable algoritma** (kural/popülerlik ilk; sonradan ML/harici). Öneri servisi API'si.

### Faz H — Push Notification (FCM) + Deep Link
- FCM entegrasyonu; senaryolar: terk-sepet, ürün tavsiye, kampanya, kategori bazlı, özel.
- **Zamanlama** + teslim raporları. Deep link hedefleri (ürün/koleksiyon/kampanya/landing/özel) + kanal entegrasyonu.

### Faz I — Çoklu dil (i18n)
- 6 dil (TR/EN/DE/FR/IT/ES). Dil paketleri, uygulama metinleri yönetimi, dil aktif/pasif.
- İçerikler Shopify dil yapılarına bağlı görüntülenir.

### Faz J — Rol & yetkilendirme
- Roller: **İçerik Editörü / Yayın Yöneticisi / Yönetici** (Keycloak realm rolleri + izinler).
- Hazırlama ↔ yayınlama ayrımı (Faz C içerik döngüsüyle hizalı).

### Faz K — Analitik & ölçümleme
- Firebase Analytics + GA4 event modeli, ingestion; e-ticaret/kampanya/dönüşüm/davranış raporları.
- Reporting servisini bu event modeline evir.

### Faz L — AI Asistanı
- **`IAiProvider` soyutlaması** (OpenAI / Google Gemini / **Anthropic Claude** / Azure OpenAI) —
  `IPaymentProvider` deseninin aynısı; sağlayıcı config + kota/maliyet.
- Senaryolar: ürün keşfi, öneri, karşılaştırma, kategori yönlendirme, hediye önerisi, doğal dil ile arama.

### Faz M — Standart entegrasyonlar
- **Judge.me** (ürün puan/yorum/foto) read entegrasyonu; **FCM** (Faz H ile).

### Faz N — Opsiyonel modüller (sonraya)
- Çoklu-mağaza yönetimi, A/B test, gelişmiş segmentasyon, AI banner generator, pazarlama otomasyonu,
  **Klaviyo**, attribution (**Appsflyer/Adjust**), CRM (Zendesk/Intercom/HubSpot/Salesforce), gelişmiş AI modülleri.

### Faz O — K8s sertleştirme (eski Faz 6)
- Helm chart'lar, autoscaling, liveness/readiness probe'lar, observability, yük testi.
- **Compose startup-race** kalıcı fix (healthcheck `depends_on` koşulları).

## 5. Kavram sözlüğü değişimi

| Eski (pazaryeri) | Yeni (deneyim platformu) |
|---|---|
| Merchant (satıcı) | Store (Shopify mağazası) = tenant |
| Offer (teklif) / master | Shopify Product (read-model) |
| Kendi Order + saga | Shopify Order (read-model) |
| Kendi Payment | Shopify Checkout (harici) |
| Komisyon/ciro raporu | Deneyim/dönüşüm analitiği |

## 6. Açık sorular / riskler
- Shopify API sürümü, scope'lar ve rate-limit stratejisi (Faz B).
- "Sürükle-bırak" bileşen şeması: esneklik vs. tip güvenliği (JSON schema + versiyonlama).
- Kişiselleştirme motoru: ilk sürüm kural-bazlı mı, harici öneri servisi mi?
- Analitik: event'ler mobil SDK'den mi (Firebase/GA4) yoksa backend ingestion mı — çift kaynak uzlaştırması.
