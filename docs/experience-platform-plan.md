# Shopify Mobil Deneyim & Kişiselleştirme Platformu — Gap Analizi & Fazlı Yol Haritası

> Kaynak: müşteri dökümanı "SHOPIFY MOBİL DENEYİM VE KİŞİSELLEŞTİRME PLATFORMU" (12 sayfa).
> Karar (2026-07-22): mevcut kod tabanı bu spesifikasyona **dönüştürülecek**; önce zorunlu çekirdek
> (Faz A–L), opsiyonel modüller (Faz M) sonraya. **Yapay zeka (AI asistanı ve AI modülleri) kapsam DIŞI —
> ilgili faz tamamen kaldırıldı.** **Mobil uygulama (React Native) ve web admin UI
> kapsam dışı**; yalnızca backend/REST API'ler (mobilin ve web panelin *backing* servisleri) inşa edilir.

## 0. 🔴 KAPSAM DÜZELTMESİ (2026-07-23): Shopify opsiyonel bir veri kaynağıdır

Her merchant Shopify kullanmak zorunda **değildir**. Ürünlerini ve **kategorilerini** manuel olarak
veya **Excel/CSV içeri aktarımla** yönetmek isteyebilir. Shopify, zorunlu bir bağımlılık değil,
**opsiyonel bir entegrasyondur** ("varsa kolayca bağlansın").

**Mevcut kodda Shopify'a sert bağımlı olan yerler (doğrulandı):**
1. **Mağaza aktivasyonu** — `Pending` → `Active` yalnızca Shopify bağlanınca; Shopify'sız mağaza kalıcı Pending.
2. **Mobil katalog** — `/api/shopify/products` okuyor; Shopify'sız mağazada katalog boş.
3. **Mobil checkout** — Shopify sepet bağlantısı üretiyor; entegrasyon yoksa 409 → satın alma yolu yok.
4. **CMS içerik doğrulaması** — referansları Shopify read-model'ine karşı doğruluyor; Shopify'sız
   mağazada tüm ürün/koleksiyon referansları kırık sayılır ve **yayınlama engellenir**.

**Hedef mimari:** Platformun kendi **Katalog servisi** (mağaza başına ürün + varyant + kategori;
her kayıtta `Source`: `manual` / `excel` / `shopify`). Manuel CRUD, Excel içeri aktarım ve Shopify
senkronu bu kataloğu **besler**; Mobil API, CMS doğrulaması ve kişiselleştirme ortak kataloğu okur.
Aktivasyon Shopify'dan koparılır. (Faz B'de silinen Catalog servisi geri gelir — ancak pazaryeri
master/offer semantiğiyle değil, mağaza-başına katalog olarak.)

**KARAR (2026-07-23, kullanıcı): Ödeme ve checkout PLATFORMUNDUR — Shopify'ın değil.**

> "shopify ödeme için kullanmayacağım. ödeme için ben pazaryeri olarak bir firmayla iyzico ile
> anlaşacağım. shopify sadece ürün stok kategori vs yönetimi için müşteriye kolaylık olsun diye istiyorum."

Bu, kaynak dökümanın **çekirdek varsayımını tersine çevirir** ("Ödeme süreçleri Shopify Checkout
üzerinden yürütülür" → GEÇERSİZ). Sonuçları:

**Shopify'ın rolü daraldı:** yalnızca **ürün / stok / kategori içeri aktarım kaynağı**. Checkout,
sipariş, ödeme, müşteri kimliği Shopify'da DEĞİL. `IShopifyClient`'ın sipariş/müşteri/indirim
senkronu ikincil önemde; asıl değer ürün+stok+koleksiyon senkronunda.

**Faz B'de silinen servisler geri geliyor.** Hepsi git geçmişinde (`6e28646^`) duruyor:
Payment (+`IPaymentProvider`, `PaymentProviderResolver`), Order (+saga, telafi tüketicileri),
Inventory (+rezervasyon), sepet (BFF/Redis → Mobil API'ye taşınır), komisyon (`Merchant.CommissionRate`),
Reporting. **Ancak birebir geri alma YANLIŞ olur** — aşağıdaki fark yüzünden.

**🔑 Kritik tasarım farkı — iyzico "Pazaryeri" modeli:** Eski Payment servisi iyzico'yu *merchant
başına* çözüyordu (her mağazanın kendi iyzico hesabı; `IyzicoPaymentProvider`: "Merchant config'inden
çözülür"). Yeni modelde **sözleşme platformundur**: tek üye işyeri hesabı, mağazalar ise
**alt üye işyeri (submerchant)**. Bunun getirdikleri:
- **Mağaza kaydı ağırlaşır:** submerchant açmak için tüzel kişilik verisi gerekir — vergi no/TCKN,
  IBAN, adres, işyeri tipi (şahıs / limited / A.Ş.). Yani onboarding'e bir **KYC adımı** eklenir.
  Mevcut self-service kayıt (J-07) bunu toplamıyor → genişletilmeli.
- **Çoklu satıcı checkout basitleşir:** iyzico sepet kalemleri kalem başına `subMerchantKey` +
  `subMerchantPrice` taşır → **tek ödeme** birden fazla mağazayı kapsayabilir. Eski kodun
  "checkout'u merchant başına böl" yaklaşımı gereksiz; ödeme tek, **sipariş/kargo** mağaza başına bölünür.
- **Komisyon = toplam tutar − Σ(submerchant tutarı)**; hakediş/ödeme aktarımı ve mutabakat raporu gerekir.
- `IPaymentProvider` soyutlaması korunur (desen doğru), ama çözümleme merchant'tan **platforma** taşınır.

**Müşteri kimliği (D-10) yeniden yazılır:** Pazaryeri müşterisi Shopify müşteri hesabıyla DEĞİL,
**platformun kendi kimliğiyle** doğrulanır (üye ol / giriş / misafir sepeti). Bu, üç kullanıcı tipi
modeliyle de tutarlı hale gelir.

**Katalog:** platformun kendi katalog servisi; kaynak `manual` / `excel` / `shopify`. Stok artık
salt-okunur bir Shopify yansıması değil — **rezervasyon** yapılabilir olmalı (Inventory geri geliyor).
Shopify'dan gelen stok, senkron kaynağı olarak Inventory'yi besler.

**Efor etkisi:** çekirdek 311 adam-gün → **~380–400 adam-gün**. Excel/Word teklif dosyaları bu
düzeltmeyi YANSITMIYOR (kullanıcı o an güncelleme istememişti) — müşteriye gönderilmeden önce
revize edilmeli.

**KARAR — Kiracılık: TEK PAZARYERİ.** Kiracı = pazaryerinin kendisi; mağazalar **alt varlıktır**.
Tek mobil uygulama vardır. CMS içeriğini pazaryerinin kendi içerik yöneticileri yönetir (mağazalar değil).

**KARAR — Ürün modeli: BARKOD MASTER + SATICI TEKLİFİ.** Ortak ürün kartı barkodla eşleşir; her
mağaza kendi fiyat/stok teklifini açar. Müşteri tek ürün sayfasında satıcıları karşılaştırır
(Trendyol/Hepsiburada modeli). Excel içeri aktarımda **barkod zorunlu alandır**; Shopify senkronu da
barkod üzerinden master'a bağlanır.

### 0.1 Kapsamlama (scoping) modeli — en büyük refactor

Bugün `TenantId` = **mağaza**; global sorgu filtreleri buna göre kurulu. Yeni modelde iki ayrı
boyut gerekir:

| Boyut | Anlamı | Uygulandığı veri |
|---|---|---|
| `TenantId` | **Pazaryeri** (şimdilik tek kayıt) | her şey — SaaS kapısını açık tutar, sonradan göç gerekmez |
| `StoreId` | **Mağaza** (yeni boyut) | satıcı teklifi, stok, sipariş kalemi, kargo, mağaza kullanıcıları |

- **CMS içeriği** (sayfa, bileşen, sürüm, bayrak, medya): yalnızca `TenantId` → pazaryeri yönetir.
  *Mevcut kodda mağaza bazlı; bu değişmeli.*
- **Ürün master** (barkod): yalnızca `TenantId` → ortak katalog.
- **Satıcı teklifi / stok / sipariş kalemi / kargo**: `TenantId` + `StoreId`.
- Mağaza kullanıcısı yalnızca kendi `StoreId`'sini görür; pazaryeri personeli hepsini görür.
  Faz J'de yazılan **`X-Acting-Store`** başlığı bu modelde tam yerine oturur: pazaryeri personeli
  bir mağaza adına işlem yapar.

**Üç kullanıcı tipinin karşılığı:**
1. **Müşteri** — pazaryerinin kendi kimliği (üye ol/giriş/misafir); mağaza kapsamı yok. *(yeni)*
2. **Pazaryeri personeli** — `owner`, `platform-admin`, `content-editor`, `publish-manager`.
   *Mevcut kodda içerik rolleri mağaza bazlı; platform seviyesine taşınmalı.*
3. **Mağaza kullanıcıları** — `store-admin` + alt roller; `StoreId` ile sınırlı.

### 0.2 Revize yol haritası

Bağımlılık sırasıyla (⟳ = geri getirilecek, git `6e28646^`):

| # | İş | Durum | ~Efor |
|---|---|---|---|
| R1 | Kapsamlama refactor'ü: `TenantId`=pazaryeri, `StoreId` boyutu; CMS ve içerik rollerini platform seviyesine taşı | ✅ dilim 1 (2026-07-23) | 10–14 |
| R2 | **Katalog servisi**: ürün master (barkod) + kategori ağacı + satıcı teklifi | ✅ dilim 1 (2026-07-24) | 18–22 |
| R3 | Manuel ürün/kategori CRUD + **Excel/CSV içeri aktarım** (doğrulama + hata raporu) | ✅ (2026-07-24) | 12–15 |
| R4 | Shopify senkronunu katalog **besleyicisine** çevir (barkod eşleme, `Source`) | uyarla | 8 |
| R5 | Müşteri kimliği (üye ol/giriş/misafir sepeti) — D-10 yeniden yazımı | yeni | 12 |
| R6 | Sepet (BFF/Redis'ten Mobil API'ye taşı, teklif bazlı) | ⟳ uyarla | 8 |
| R7 | Stok + rezervasyon (teklif seviyesinde; Shopify stok besler) | ⟳ uyarla | 10 |
| R8 | Sipariş: tek ödeme → mağaza başına sipariş bölme | ⟳ uyarla | 15 |
| R9 | **iyzico Pazaryeri**: submerchant kaydı + KYC onboarding + kalem bazlı ödeme | ⟳ yeniden tasarla | 22–26 |
| R10 | Komisyon, hakediş, ödeme aktarımı, mutabakat raporu | ⟳ uyarla | 15 |
| R11 | Kargo yönetimi (mağaza başına taşıyıcı, gönderi takibi) | yeni | 14 |


#### R1 dilim 1 tamamlandı (2026-07-23)

**Yapılan:**
- **Terminoloji/kapsam:** `ITenantContext` → `IStoreContext` (`StoreId`, `SetStore`); `ITenantOwned` →
  `IStoreOwned`; `AuditableTenantEntity` → `AuditableStoreEntity` + yeni mağazasız `AuditableEntity`;
  `TenantResolutionMiddleware` → `StoreResolutionMiddleware`. JWT claim'i `tenant_id` → **`store_id`**
  (realm mapper + kullanıcı profili + Admin API sorguları + tüm servisler).
- **CMS içeriği pazaryeri seviyesine indi:** Page/PageVersion/PreviewToken/MediaAsset/FeatureFlag/
  ExperienceSnapshot artık mağaza kolonu taşımıyor; benzersiz indeksler (handle, flag key, snapshot
  sürümü) global. Medya klasörü mağaza yerine ay bazlı.
- **Roller iki hata ayrıldı:** içerik hattı (content-editor ⊂ publish-manager ⊂ platform) ile mağaza
  hattı (store-admin ⊂ platform). Mağaza rolleri içerik uçlarından TAMAMEN çıkarıldı (okuma dahil).
  Middleware'de içerik ekibi mağaza claim'i yoksa platform kapsamında okur.
- **Deneyim kamusal:** CMS `/api/experience/current` ve Mobil `/api/mobile/experience/*` anonim —
  giriş yapmamış pazaryeri müşterisi uygulamayı açabilir. Önbellek tek girdiye indi.
- **Denetim:** `AuditEntry.StoreId` null olabilir (platform işlemleri); içerik denetimi
  `content.publish` politikasına, hesap denetimi `store.manage`e bağlandı (parametreli
  `MapAuditEndpoints`).
- Migration'lar: `R1PlatformContent` (CMS) + `R1StoreScope` (Merchant/ShopifySync/Mobile). Dev
  `.data` sıfırlandı (realm yeniden import → `store_id`).

**Yakalanan güvenlik açığı:** mağaza yöneticisi `/api/users` ile kullanıcısına `content-editor`
atayabiliyordu → içerik rolleri artık platform yetkisi olduğundan bu, pazaryeri CMS'ine yetki
sızıntısı olurdu. `AssignableRoles` mağaza-seviyesi rollerle sınırlandı (şimdilik yalnız
`store-admin`). Smoke doğruladı: içerik rolü atama 400, store-admin daveti 201.

**Bilinçli ARA durumlar:** mobil katalog uçları hâlâ mağaza-kapsamlı ShopifySync read-model'ini
okuyor ve kimlik istiyor (R2/R4'te ortak kataloğa geçip anonimleşecek); checkout hâlâ Shopify
permalink üretiyor (R8'de platform checkout'una dönecek); `Merchant` → `Store` toplu tip/rota
adlandırması kozmetik olarak ertelendi.

**Smoke (smokeR1):** store_id claim ✓, editör 403/yayıncı 200 ✓, merchant içerik 403 (okuma dahil) ✓,
anonim deneyim 200 + Home çözümü ✓, anonim sayfa yönetimi 401 ✓, içerik rolü sızıntısı 400 ✓,
audit ayrımı (403/200) + yayın kaydı StoreId=null ✓, X-Acting-Store 200 ✓, Shopify bağla →
mutabakat senkronu 5 ürün/2 koleksiyon/4 sipariş/3 müşteri/3 indirim/4 sayfa ✓.


#### R2 dilim 1 tamamlandı (2026-07-24)

**Yeni servis: `Marketplace.Catalog.Api`** (port 8091, şema `catalog`, eski 7a master+offer kodundan
R1 sözleşmesine uyarlandı). İki katman:
- **Pazaryeri katmanı** (mağaza filtresi yok): `Product` (barkod benzersiz; kartı ilk ekleyen
  oluşturur, sonrakiler kartın alanlarını EZEMEZ — zenginleştirme/moderasyon platforma ait) +
  `Category` ağacı (ParentId; taksonomiyi yalnız platform yönetir, silme kullanımdaysa pasifleştirir).
- **Mağaza katmanı** (StoreId filtreli): `Offer` — fiyat/stok/SKU, StoreId+ProductId benzersiz,
  `Source` = manual/excel/shopify. `StockQuantity` R7'de rezervasyonlu Inventory'ye devredilecek.

**Uçlar:** kamusal (anonim) `/api/catalog/products` liste/arama/sıralama + `/{id}` satıcı kıyası
(ucuz önde) + `/barcode/{barcode}`; `/api/catalog/categories` (okuma anonim, yazma owner);
`/api/store/products` (store.manage) — barkodla upsert, listeleme, güncelleme, satıştan kaldırma
(pasifleştirme); `/api/audit/catalog` denetim.

**Kritik EF detayı:** Offer'daki mağaza query filter'ı navigation üzerinden kamusal sorgulara da
uygulanır — anonim istekte tüm teklifler elenir ve vitrin boş kalırdı. Kamusal uçlar
`IgnoreQueryFilters` ile bilinçli olarak mağazalar-arası okur.

**Smoke (smokeR2, tümü ilk koşuda geçti):** kategori ağacı + taksonomi yetkisi ✓; mağaza-1 kart
oluşturur, mağaza-2 AYNI barkodla karta katılır (kart adı ezilmez) ✓; anonim vitrin satıcı=2,
en iyi fiyat doğru ✓; mağaza izolasyonu (başkasının teklifine 404) ✓; anonim 401 / içerik
editörü 403 ✓; owner X-Acting-Store ile mağaza adına fiyat günceller ve denetimde "mağaza adına"
işaretlenir ✓; teklif çekilince satıcı sayısı düşer, kart kalır ✓.

**Ertelenen:** varyant desteği (beden/renk) — master model bilinçli tek-SKU başladı; kart
zenginleştirme/moderasyon uçları; mobil kataloğun bu servise bağlanması (R4 ile birlikte).


#### R3 tamamlandı (2026-07-24)

**Excel/CSV toplu içeri aktarım** — Shopify kullanmayan mağazanın ana yolu:
- `POST /api/store/products/import` (store.manage; multipart, 2MB / 5000 satır sınırı) +
  `GET /api/store/products/import/template` (örnek CSV, anonim).
- **Parser toleransı:** CSV ayracı başlıktan koklanır (Türkçe Excel `;` ile dışa aktarır),
  tırnaklı alanlar desteklenir; Türkçe kolon başlıkları (Barkod/Fiyat/Stok/Marka/Kategori...)
  İngilizce karşılıklarına eşlenir; ondalık hem `1.349,90` hem `1349.90` okunur.
  XLSX için ClosedXML (MIT — EPPlus bilinçli tercih EDİLMEDİ, ticari lisansa geçti).
- **Kısmi başarı:** hatalı satır diğerlerini engellemez; rapor satır numarasıyla hata VE uyarı
  listesi döner. Kategori bulunamazsa satır DÜŞMEZ — ürün kategorisiz aktarılır, uyarı verilir
  (kategori slug ya da adla eşlenir). Geçerli satırlar tek transaction'da kaydedilir; denetim
  kaydı (catalog.import, sayaçlarla) aynı işlemde yazılır.
- **Ortak iş kuralı:** `ProductUpsertService` — manuel uç (R2) ve import aynı barkod→master
  kuralını paylaşır; SaveChanges çağırmaz (toplu kayıt). `Local` önceliği sayesinde aynı dosyada
  tekrar eden barkod ikinci satırda DB'ye gitmeden karta bağlanır (son satır kazanır).

**Smoke:** TR başlıklı `;`'li CSV 5 satır → 3 aktarıldı / 2 satır hatası (boş fiyat, kısa barkod)
/ 1 kategori uyarısı ✓; tekrarlı barkodda son fiyat kazandı (2299) ✓; XLSX 2 satır ✓;
source=excel işaretlendi ✓; aynı dosya yeniden → 0 yeni kart (idempotent) ✓; şablon 200 /
anonim yükleme 401 ✓; denetim kaydı sayaçlarla ✓.

Sonrasında mevcut plandaki deneyim fazları (E banner/popup, F tema, G kişiselleştirme,
H push, I çoklu dil, K analitik, L 3. parti, N K8s) devam eder.

**Efor etkisi:** çekirdek **311 → ~430–460 adam-gün**. Excel görev listesi ve Word teklif dosyası
bu modeli yansıtmıyor; müşteriye gönderilmeden önce **yeniden üretilmeli**.


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

**J-01…J-06 — Roller ve yetkilendirme (13,5 adam-gün):**
- **J-01, J-02, J-04 ✅ (2026-07-23) — roller, izin matrisi, hazırlama↔yayınlama ayrımı:**
  Ortak yapı taşında merkezî sözlük (`Roles` / `Policies` / `AddMarketplacePolicies`), `AddKeycloakJwtAuth`
  içinden tüm servislere kurulur. Roller: `content-editor` ⊂ `publish-manager` ⊂ `store-admin`
  (eski `merchant` rolü buna eşdeğer) ⊂ `owner`/`platform-admin`. Politikalar: `content.edit`
  (sayfa/bileşen/medya hazırlama), `content.publish` (yayınlama, geri alma, bayrak değişimi,
  snapshot rebuild), `store.manage`, `owner` (mağaza açma). CMS uçları okuma/düzenleme/yayınlama
  olarak üç ayrı gruba bölündü. Realm'e roller + `demo-editor`, `demo-publisher` kullanıcıları eklendi.
  Doğrulandı (smokeH): editör hazırlar ve medya yükler ama **publish/bayrak/rebuild → 403**;
  yayın yöneticisi yayınlar ve düzenler; mağaza açma yalnızca owner (editör 403, owner 201).
- **J-03 ✅ (2026-07-23) — kullanıcı yönetimi + mağaza adına işlem:**
  - **Keycloak Admin entegrasyonu:** realm'e confidential client `marketplace-admin-api`
    (service account: manage-users/view-users/query-users). `IKeycloakAdminClient` token
    önbellekli; kullanıcı listeleme (mağazaya göre `q=tenant_id:`), oluşturma, rol atama
    (tek rol modeli: eskisi kaldırılır), aktif/pasif, şifre sıfırlama e-postası.
  - **Uçlar:** `/api/users` (liste, davet, rol değiştir, aktifleştir/pasifleştir, şifre sıfırla)
    ve `/api/users/roles` — hepsi `store.manage` yetkisinde. Hedef kullanıcının aynı mağazaya
    ait olduğu doğrulanır (başka mağazanın kullanıcısına müdahale → 403).
  - **Mağaza adına işlem (J-04):** platform personelinin `tenant_id` claim'i yoktur; artık
    `X-Acting-Store` başlığıyla bir mağazanın kapsamına girer (`TenantResolutionMiddleware`).
    Bu olmadan yaptığı yazma işlemleri **sahipsiz kayıt** üretiyordu. Başlık yalnızca platform
    rolleri için geçerlidir — mağaza kullanıcısı gönderse bile yok sayılır (kapsam kaçışı yok).
  - **YAKALANAN HATA (Keycloak 26):** "declarative user profile" tanımlanmamış öznitelikleri
    **sessizce siler**; Admin API ile açılan kullanıcının `tenant_id`'si kayboluyor ve kullanıcı
    mağazasız kalıyordu. Realm user profile'ına `tenant_id` tanımlandı (+ `unmanagedAttributePolicy: ENABLED`)
    ve kod, oluşturma sonrası özniteliği doğrulayıp kaybolursa açık hata veriyor.
  - Doğrulandı (smokeI): davet→rol değiştir→pasifleştir/aktifleştir; editör 403; platform mağaza
    seçmeden 400; acting-store ile yazılan sayfa **doğru mağazada** göründü; mağaza kullanıcısı
    başlıkla kapsam değiştiremedi.
  - **Operasyonel not:** Keycloak `--force-recreate` sonrası imzalama anahtarları değişir;
    servislerin JWKS önbelleği bir süre bayat kalıp 401 üretebilir (kendiliğinden düzelir).
- **J-05 ✅ (2026-07-23) — denetim kaydı (audit log):**
  Ortak yapı taşı (`BuildingBlocks/Auditing`, Outbox desenini izler): `AuditEntry` (aktör, roller,
  mağaza, **mağaza adına mı**, işlem, varlık, özet, zaman) + `modelBuilder.AddAuditLog(filtre, şema)` +
  `IAuditLogger.Record(...)`. Kayıt, iş verisiyle **aynı SaveChanges** içinde yazılır — işlem geri
  alınırsa denetim kaydı da oluşmaz.
  **Kaydedilen işlemler:** CMS → `page.publish`, `page.restore`, `page.delete`, `flag.changed`,
  `media.upload`; Merchant → `store.signup`, `user.invited`, `user.role_changed`, `user.deactivated`.
  **Sorgu:** `GET /api/audit` (içerik) ve `GET /api/audit/account` (hesap) — `store.manage` yetkisi,
  mağaza kapsamına göre filtreli.
  **YAKALANAN HATA (EF model önbelleği):** kiracı filtresine `ITenantContext` **parametre olarak**
  geçirilince, model bir kez kurulup önbelleğe alındığı için ilk isteğin tenant nesnesi closure'da
  donuyor ve sonraki isteklerde filtre hiçbir kaydı döndürmüyordu (kayıtlar yazılıyor ama görünmüyordu).
  Çözüm: filtre ifadesi çağırandan alınır ve **DbContext alanını** (`_tenant`) referans eder —
  EF bunu her sorguda yeniden değerlendirir (diğer entity'lerdeki mevcut desen).
  Doğrulandı (smokeK): yayının `publish-manager` tarafından yapıldığı, platform personelinin
  "mağaza adına" işleminin işaretlendiği, editörün denetim kaydını göremediği (403) ve
  mağazalar arası izolasyon.
- **Kalan:** J-06 testler.

**J-07…J-10 — Üyelik ve kimlik akışları (+15 adam-gün, plana yeni eklendi):**
- **J-07 ✅ (2026-07-23) — Mağaza self-service kaydı ve sağlama:**
  `POST /api/signup` **anonimdir** (henüz hesap yok) ve **hız sınırına tabidir** (IP bazlı sabit
  pencere; prod 5/10dk, compose'da dev için gevşetildi). Akış: müsaitlik kontrolü → mağaza kaydı
  (`Pending`) → Keycloak'ta `store-admin` kullanıcısı (`tenant_id` ile) → yanıtta "sonraki adım:
  Shopify bağla". `GET /api/signup/availability` form doğrulaması için.
  **Atomiklik:** iki ayrı sistem yazıldığından kullanıcı oluşturma başarısız olursa mağaza kaydı
  **telafi edilir**; telafi de başarısız olursa sahipsiz kayıt açıkça loglanır.
  **Komisyon oranını mağaza kendisi belirleyemez** — platform varsayılanı uygulanır
  (`Platform:DefaultCommissionRate`). **Aktivasyon:** Shopify entegrasyonu yapılandırılınca
  mağaza `Pending` → `Active`.
  **YAKALANAN HATA:** parola her zaman "geçici" işaretleniyordu → Keycloak `UPDATE_PASSWORD` zorunlu
  eylemi ekliyor ve yeni sahip giriş yapamıyordu ("Account is not fully set up"). Artık kayıtta parola
  kalıcı (sahip kendi belirler), davet edilen kullanıcıda geçici (ilk girişte değiştirir).
  Doğrulandı (smokeJ): kayıt → giriş (store-admin + tenant_id) → mağazasını görme → çakışma 409 →
  geçersiz girdi 400 → Shopify bağla → **Active** → ekibe kullanıcı ekleme → veri izolasyonu.
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
