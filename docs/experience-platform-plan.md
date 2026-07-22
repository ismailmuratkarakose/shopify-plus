# Shopify Mobil Deneyim & Kişiselleştirme Platformu — Gap Analizi & Fazlı Yol Haritası

> Kaynak: müşteri dökümanı "SHOPIFY MOBİL DENEYİM VE KİŞİSELLEŞTİRME PLATFORMU" (12 sayfa).
> Karar (2026-07-22): mevcut kod tabanı bu spesifikasyona **dönüştürülecek**; önce zorunlu çekirdek
> (Faz A–L), opsiyonel modüller (Faz M) sonraya. **Yapay zeka (AI asistanı ve AI modülleri) kapsam DIŞI —
> ilgili faz tamamen kaldırıldı.** **Mobil uygulama (React Native) ve web admin UI
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
| `IPaymentProvider` + resolver **deseni** | ✅ Desen olarak | sağlayıcı-soyutlaması gereken yerlere şablon |
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

## 4. Fazlı yol haritası (çekirdek A–L, opsiyonel M; UI ve AI hariç)

### Faz A — Yeniden hizalama & Shopify bağlantısı  🟡 (bağlantı ✅)
- Kavram: tenant = Shopify mağazası. Merchant→Store terminolojisi (kavramsal; toplu rename ertelendi).
- **Shopify OAuth akışı ✅ (2026-07-22):** `IShopifyOAuth` (simulator token üretir / graphql authorize+exchange);
  `POST /api/shopify/connect` (mağaza sahibi, JWT tenant) + `GET /shopify/oauth/callback` (anonim, graphql).
  Token Merchant'a **internal write** (`POST /internal/integrations/{id}/shopify`) ile kaydedilir →
  mevcut `MerchantIntegrationConfigured` pipeline'ı read-model'i senkronlar (secret bus'a düşmez).
  Doğrulandı (smokeA): connect → şifreli token → entegrasyon aktif (maskeli `****2bff`).
- **Pazaryeri yapısı SİLİNDİ ✅:** Catalog/Inventory/Order/Payment/Reporting/BFF projeleri, compose/gateway
  girdileri, DB'leri ve pazaryeri event'leri kaldırıldı (arşiv değil, silme kararı). Kalan çekirdek:
  Merchant + ShopifySync + Gateway + BuildingBlocks + Contracts.
- **Bilinen kenar durum:** `CreateMerchant` yalnızca slug benzersizliğini kontrol ediyor; var olan Id ile
  farklı isim → PK çakışması (500 yerine 409 olmalı). Ayrı görev.

### Faz B — Shopify senkron genişletme (read-model)  ✅ TAMAMLANDI
- Varlıklar: **ürün, varyant, koleksiyon, stok*, fiyat*, sipariş, müşteri, indirim, sayfa** ✅
  (*stok/fiyat varyantta geliyor).
- **Dilim 4 ✅ (2026-07-22) — indirim + sayfa + mutabakat:** `GetDiscountsAsync`/`GetPagesAsync`
  (simulator 3 indirim / 4 sayfa); read-model `SyncedDiscount`, `SyncedPage`; uçlar
  `GET /api/shopify/discounts` (`status` filtresi), `/pages`, `/pages/{handle}`.
  **Senkron durumu:** `StoreSyncState` (son çalışma, tetikleyici, süre, sayaçlar, hata) +
  `GET /api/shopify/sync/status`. **Periyodik mutabakat:** `ReconciliationService` (BackgroundService)
  bağlı tüm mağazaları yapılandırılabilir aralıkla yeniden senkronlar
  (`Sync:Reconciliation:{Enabled,IntervalMinutes,InitialDelaySeconds}`; prod 60 dk, compose'da dev 1 dk).
  Migration: ShopifyDiscountsPagesSyncState. Doğrulandı (smokeD): 6 varlık senkronu + otomatik mutabakat.
- **Dilim 3 ✅ (2026-07-22) — sipariş + müşteri:** `GetOrdersAsync`/`GetCustomersAsync` (simulator 4 sipariş/
  3 müşteri, ürünlere referanslı); read-model `SyncedOrder`(+`SyncedOrderLine`) ve `SyncedCustomer`;
  `StoreSyncService` full-refresh'e dahil; uçlar `GET /api/shopify/orders` (filtre: `customerId`,
  `financialStatus`), `/orders/{id}`, `/customers` (arama). Migration: ShopifyOrdersCustomers.
  Doğrulandı (smokeC). **Siparişler SALT OKUNUR** — checkout Shopify'da.
- **Dilim 1 ✅ (2026-07-22):** ShopifySync = "Store Data" servisi. `IShopifyClient`'a read (`GetProductsAsync`/
  `GetCollectionsAsync`, simulator deterministik katalog + graphql stub); read-model entity'leri
  (`SyncedProduct`/`SyncedVariant`/`SyncedCollection`, tenant=mağaza); `StoreSyncService` (credential çöz →
  Shopify'dan çek → full-refresh upsert); uçlar `POST /api/shopify/sync`, `GET /api/shopify/products`,
  `/products/{id}`, `/collections`. Migration: ShopifyReadModel. Doğrulandı (smokeB): sync → 5 ürün
  (varyant+sku+barkod+fiyat+stok) + 2 koleksiyon.
- **İleriye bırakılan (gerçek mağaza bağlanınca):** Shopify Admin GraphQL sorgularının gerçek implementasyonu
  (şu an simulator ile çalışıyor, `IShopifyClient` arkasında), imleçli sayfalama/bulk import,
  rate-limit + backoff. Bunlar Excel görev listesinde B-13/B-14/B-16 olarak ayrıca eforlandırılmıştır.
- Çıktı: mağaza verisinin güncel read-model'i; CMS ve kişiselleştirme bunu okur. ✅

### Faz C — Deneyim/İçerik CMS çekirdeği (sürükle-bırak veri modeli)  ✅ TAMAMLANDI
- **Dilim 1 ✅ (2026-07-22) — `Marketplace.Cms.Api` (port 8090, db `cms`):**
  - **Model:** `Page` (mantıksal ekran; ScreenType: Home/ProductList/ProductDetail/Cart/Campaign/Landing,
    mağaza içinde benzersiz `handle`) → `PageVersion` (Draft/Published/Archived) → `PageComponent`
    (tip + sıra + `jsonb` ayarlar). İçerik sayfada değil **sürümde** tutulur: taslak düzenlenirken
    yayındaki içerik değişmez.
  - **Bileşen tipleri (7):** banner, product_grid, collection, campaign, popup, personalization,
    dynamic_content — her biri zorunlu/opsiyonel ayar şemasıyla `ComponentTypes` kayıt defterinde.
    `GET /api/pages/component-types` tasarımcının paletini besler. Ekleme/güncellemede ayarlar
    doğrulanır (zorunlu alan, izinli değer listesi ve koşullu kurallar: ör. `source=manual` ise
    `productIds` zorunlu, `trigger=delay` ise `delaySeconds` zorunlu).
  - **Uçlar:** sayfa CRUD (`/api/pages`), bileşen ekle/güncelle/sil, **toplu sıralama**
    (`PUT /{id}/components/order` — sürükle-bırak arka ucu), `POST /{id}/publish`,
    `GET /{id}/versions`, `POST /{id}/versions/{versionId}/restore`, `GET /{id}/published`.
  - **Yayın döngüsü:** düzenleme daima taslakta; taslak yoksa yayındaki sürümden kopyalanarak açılır.
    Yayınlama taslağı dondurur, önceki yayını arşive alır. Geri alma eski sürümü yeni taslağa kopyalar.
  - Migration: InitialCms. Doğrulandı (smokeE): palet, 3 bileşen, 4 hatalı doğrulama senaryosu,
    sıralama, yayın, **yayın sonrası düzenlemede yayının bozulmaması**, sürüm geçmişi, geri alma.
  - **EF tuzağı:** istemcide üretilen anahtarlar (`Guid.NewGuid()` alan başlatıcısı) yüzünden, izlenen
    bir üst nesnenin koleksiyonuna eklenen yeni alt kayıtlar EF tarafından "mevcut" sayılıp INSERT
    yerine UPDATE üretiyordu → yeni nesneler DbSet'e **açıkça** eklendi (Added durumu garanti).
- **Dilim 2 ✅ (2026-07-22) — önizleme, medya, bütünlük:**
  - **Önizleme kanalı:** `POST /api/pages/{id}/preview-token` süreli, iptal edilebilir anahtar üretir;
    `GET /api/preview/{token}` **anonimdir** (test cihazı panele giriş yapmadan taslağı görür) —
    kiracıyı ve sayfayı anahtar belirler, bu yüzden `PreviewToken` üzerinde tenant filtresi yoktur.
  - **Medya servisi:** `IMediaStorage` soyutlaması + `LocalFileMediaStorage` (mağaza başına klasör;
    S3/MinIO aynı arayüzün arkasına eklenebilir). `POST /api/media` (multipart, tip+boyut doğrulaması),
    liste, silme yetkili; `GET /api/media/{id}/content` **anonim** (mobil/CDN erişimi).
  - **İçerik bütünlüğü:** `ContentValidator` bileşenlerin işaret ettiği ürün/koleksiyon/indirim
    kayıtlarını ShopifySync read-model'inden doğrular (`IStoreDataClient`, JWT forward).
    `GET /api/pages/{id}/validate` sorunları listeler; **yayınlama kırık referans varsa 409 ile engellenir**.
    Mağaza verisine ulaşılamazsa yalnızca *uyarı* üretilir — altyapı arızası yayını kilitlemez.
  - Migration: CmsPreviewAndMedia. Doğrulandı (smokeF): medya yükleme/anonim erişim/tip reddi,
    geçerli içerik doğrulaması, silinmiş koleksiyon tespiti, yayın engelleme, düzeltip yayınlama,
    anonim önizlemede yayınlanmamış taslağın görünmesi, geçersiz anahtarda 404.
- Çıktı: web panelin çağıracağı CMS API'leri (UI ayrı ekip). ✅

### Faz D — Remote Config + Mobile Experience API  ✅ TAMAMLANDI (2026-07-22)
- **CMS tarafı:** `FeatureFlag` (aç/kapa + değer) CRUD; **`ExperienceSnapshot`** — yayınlanan içeriğin
  DEĞİŞMEZ, sürümlü anlık görüntüsü. Her yayın veya bayrak değişikliği yeni sürüm doğurur.
  Uçlar: `/api/flags`, `GET /api/experience/current` (**ETag = sürüm**, `If-None-Match` → 304),
  `/api/experience/versions`, `/rebuild`. Mobil taraf CMS'in iç modeline değil bu sabit sözleşmeye bağlanır.
- **Yeni servis `Marketplace.Mobile.Api`** (port 8088, db `mobile`):
  - `GET /api/mobile/experience/` — açılış çağrısı: sürüm + bayraklar + ekran listesi.
  - `GET /api/mobile/experience/{ekran}` — ekran düzeni (ScreenType veya handle ile çözülür), **ETag**'li.
  - **Snapshot önbelleği** (`ExperienceCache`, mağaza başına, TTL sonrası ETag ile yeniden doğrulama);
    CMS'e ulaşılamazsa bayat sürümle devam edilir → mobil ekran boş kalmaz.
  - Katalog: `/products` (arama, marka, fiyat aralığı, sıralama, sayfalama), `/products/{id}`
    (görüntüleme "son gezilenler"e yazılır), `/collections`, `/collections/{id}/products`.
  - Kullanıcı listeleri: `/favorites` (ekle/çıkar/listele), `/recently-viewed` — kalıcı, kullanıcı+mağaza bazlı.
  - `POST /api/mobile/checkout` → **Shopify sepet bağlantısı** üretir; ödeme Shopify Checkout'ta tamamlanır.
- **Tekil ekran kısıtı (hata düzeltmesi):** Ana Sayfa/Ürün Listeleme/Ürün Detay/Sepet ekranları mağaza
  başına TEK olabilir (`ScreenTypes.IsSingleton`); aksi hâlde mobilde hangi sayfanın gösterileceği
  belirsizdi ve eski sayfa dönüyordu. İkinci deneme 409; kampanya/landing çoklu kalır.
- Migration'lar: CmsExperienceAndFlags, InitialMobile. Doğrulandı (smokeG): yayın→snapshot(v4)→bayrak(v5)
  →mobil ekran→304 önbellek→katalog/arama/filtre→detay→koleksiyon→favori→checkout URL→yeni yayın(v6)
  mobilde göründü.
- **AÇIK KALEM → D-10 · Müşteri kimliği ve oturum (3 → 6 adam-gün, revize 2026-07-23):**
  Mobil uygulamanın son kullanıcıları **bizim Keycloak'ımızda değil, Shopify müşteri altyapısında** olur
  (üye kayıt, giriş, şifre sıfırlama, profil, adres, sipariş geçmişi, misafir senaryosu). Müşteri zaten
  web mağazasında Shopify hesabına sahiptir; kimliği kopyalamak sipariş geçmişini böler.
  **Bağımlılık:** gerçek Shopify uygulaması kurulmadan yapılamaz (bkz. A-08).
  **Çözülmesi gereken mimari karar (efor artışının sebebi):** bugün mobil uçlar kiracıyı JWT'deki
  `tenant_id` claim'inden çözüyor; gerçek müşteride böyle bir token olmayacak. Netleşmesi gerekenler:
  - mağaza (tenant) kimliği nereden gelecek → uygulamaya gömülü **mağaza API anahtarı / app identifier**,
  - müşteri kimliği nereden → **Shopify customer access token** doğrulanıp `UserRef`'e eşlenecek,
  - misafir kullanıcı nasıl çalışacak → favoriler/son gezilenler cihaz bazlı mı tutulacak.
  Geliştirme sırasında müşteri yerine geçici olarak Keycloak demo kullanıcısının token'ı kullanılmaktadır.

### Faz D — kapsam notları (özgün plan)
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

### Faz J — Kimlik, Üyelik & Yetkilendirme
> Kapsam genişletildi (2026-07-23): yalnızca roller değil, **panel tarafının kimlik/üyelik akışlarının tamamı**.
> Gerekçe: mevcut durumda Keycloak realm'inde 3 elle tanımlı demo kullanıcı var; self-service kayıt,
> şifre sıfırlama ve e-posta doğrulama kapalı; mağazayı platform yöneticisi elle açıyor; kimlik doğrulama
> geliştirme amaçlı **password grant** ile yapılıyor. Bunlar canlıya çıkamaz.

**J-01…J-06 — Roller ve yetkilendirme (13,5 adam-gün, mevcut plan):**
- Roller: **İçerik Editörü / Yayın Yöneticisi / Yönetici** (Keycloak realm rolleri + izin matrisi).
- Kullanıcı yönetim uçları (davet, rol atama, pasifleştirme), denetim kaydı.
- Hazırlama ↔ yayınlama ayrımı (Faz C içerik döngüsüyle hizalı).

**J-07…J-10 — Üyelik ve kimlik akışları (+15 adam-gün, plana yeni eklendi):**
- **J-07 · Mağaza self-service kaydı ve sağlama (provisioning) — 5 gün.** Kayıt formu API'si →
  Keycloak Admin API ile kullanıcı + `tenant_id` claim üretimi → Merchant (mağaza) kaydı →
  ilk girişte Shopify bağlama akışına yönlendirme. Bugün bu adım elle yapılıyor.
- **J-08 · Üretim kimlik akışı (Authorization Code + PKCE) — 4 gün.** Panel ve mobil için standart
  yetkilendirme kodu akışı, refresh token yönetimi, oturum sonlandırma; **password grant'in kaldırılması**
  (şu anki kullanım yalnızca geliştirme içindir).
- **J-09 · Şifre sıfırlama, e-posta doğrulama, SMTP — 3 gün.** Keycloak'ta ilgili akışların açılması,
  e-posta sağlayıcı yapılandırması, çoklu dil e-posta şablonları.
- **J-10 · Keycloak üretim sertleştirmesi — 3 gün.** Token ömürleri, brute-force koruması, ortam bazlı
  client secret yönetimi, realm tanımının sürümlenmesi/otomasyonu.

> **Faz J toplamı: 13,5 + 15 = 28,5 adam-gün.** (Görev listesi Excel'i bu genişletmeden önce üretildiği için
> orada Faz J 13,5 gün görünür; müşteriye güncel rakam verilirken bu fark dikkate alınmalıdır.)

### Faz K — Analitik & ölçümleme
- Firebase Analytics + GA4 event modeli, ingestion; e-ticaret/kampanya/dönüşüm/davranış raporları.
- Reporting servisini bu event modeline evir.

### Faz L — Standart entegrasyonlar
- **Judge.me** (ürün puan/yorum/foto) read entegrasyonu; **FCM** (Faz H ile).

### Faz M — Opsiyonel modüller (sonraya)
- Çoklu-mağaza yönetimi, A/B test, gelişmiş segmentasyon, pazarlama otomasyonu,
  **Klaviyo**, attribution (**Appsflyer/Adjust**), CRM (Zendesk/Intercom/HubSpot/Salesforce).

### Faz N — K8s sertleştirme (eski Faz 6)
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
