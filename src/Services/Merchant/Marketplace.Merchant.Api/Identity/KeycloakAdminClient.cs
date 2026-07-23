using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Marketplace.Merchant.Api.Identity;

public record PanelUser(
    string Id, string Username, string? Email, string? FirstName, string? LastName,
    bool Enabled, Guid? StoreId, IReadOnlyList<string> Roles);

/// <param name="Password">Başlangıç parolası (opsiyonel).</param>
/// <param name="PasswordIsTemporary">
/// true ise kullanıcı ilk girişte parolasını değiştirmek zorundadır (yönetici daveti senaryosu).
/// Kendi kaydını yapan mağaza sahibi parolayı kendisi belirlediği için false olmalıdır;
/// aksi hâlde Keycloak "Account is not fully set up" diyerek girişi reddeder.
/// </param>
public record CreatePanelUser(
    string Username, string? Email, string? FirstName, string? LastName,
    string Role, string? Password, bool PasswordIsTemporary = true);

/// <summary>
/// Panel kullanıcılarının Keycloak üzerinde yönetimi (mağaza ekibi: store-admin, publish-manager,
/// content-editor). Erişim, realm'deki service account client'ı ile client_credentials üzerinden alınır;
/// yönetici parolası uygulamada tutulmaz.
/// </summary>
public interface IKeycloakAdminClient
{
    Task<IReadOnlyList<PanelUser>> GetStoreUsersAsync(Guid storeId, CancellationToken ct);
    Task<PanelUser?> GetUserAsync(string userId, CancellationToken ct);
    Task<PanelUser> CreateStoreUserAsync(Guid storeId, CreatePanelUser request, CancellationToken ct);
    Task SetRoleAsync(string userId, string role, CancellationToken ct);
    Task SetEnabledAsync(string userId, bool enabled, CancellationToken ct);
    Task SendPasswordResetAsync(string userId, CancellationToken ct);

    /// <summary>Kullanıcı adı veya e-posta zaten kayıtlı mı (kayıt öncesi kontrol).</summary>
    Task<bool> ExistsAsync(string username, string? email, CancellationToken ct);

    /// <summary>Telafi amaçlı silme: kayıt akışının ikinci adımı başarısız olursa geri alınır.</summary>
    Task DeleteUserAsync(string userId, CancellationToken ct);
}

public sealed class KeycloakAdminClient : IKeycloakAdminClient
{
    private static readonly string[] AssignableRoles =
        ["store-admin", "publish-manager", "content-editor"];

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<KeycloakAdminClient> _logger;

    // Basit token önbelleği: her istekte yeni token almamak için (süre bitmeden yenilenir).
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public KeycloakAdminClient(HttpClient http, IConfiguration config, ILogger<KeycloakAdminClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string Realm => _config["Keycloak:Realm"] ?? "marketplace";
    private string AdminBase => $"/admin/realms/{Realm}";

    public static bool IsAssignableRole(string role) =>
        AssignableRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AssignableRoleNames => AssignableRoles;

    // --- Kimlik ---

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _token;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _config["Keycloak:AdminClientId"] ?? "marketplace-admin-api",
                ["client_secret"] = _config["Keycloak:AdminClientSecret"] ?? ""
            });

            using var resp = await _http.PostAsync($"/realms/{Realm}/protocol/openid-connect/token", form, ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            _token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 60;
            // Sınırda kalmamak için 15 saniye pay bırakılır.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, expiresIn - 15));
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return req;
    }

    // --- Kullanıcı işlemleri ---

    public async Task<IReadOnlyList<PanelUser>> GetStoreUsersAsync(Guid storeId, CancellationToken ct)
    {
        // Keycloak 26: kullanıcı özniteliğine göre arama (q=key:value).
        using var req = await AuthorizedAsync(HttpMethod.Get,
            $"{AdminBase}/users?q=tenant_id:{storeId}&max=200", ct);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var users = new List<PanelUser>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("id").GetString()!;
            users.Add(await ToPanelUserAsync(el, id, ct));
        }
        return users;
    }

    public async Task<PanelUser?> GetUserAsync(string userId, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, $"{AdminBase}/users/{userId}", ct);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return await ToPanelUserAsync(doc.RootElement, userId, ct);
    }

    public async Task<PanelUser> CreateStoreUserAsync(Guid storeId, CreatePanelUser request, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["username"] = request.Username,
            ["email"] = request.Email,
            ["firstName"] = request.FirstName,
            ["lastName"] = request.LastName,
            ["enabled"] = true,
            ["emailVerified"] = false,
            // Mağaza kimliği kullanıcı özniteliğinde tutulur; token'a `tenant_id` claim'i olarak yansır.
            ["attributes"] = new Dictionary<string, string[]> { ["tenant_id"] = [storeId.ToString()] }
        };

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            body["credentials"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "password",
                    ["value"] = request.Password!,
                    ["temporary"] = request.PasswordIsTemporary
                }
            };
        }

        using var req = await AuthorizedAsync(HttpMethod.Post, $"{AdminBase}/users", ct);
        req.Content = JsonContent.Create(body);
        using var resp = await _http.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException($"Bu kullanıcı adı veya e-posta zaten kayıtlı: {request.Username}");
        resp.EnsureSuccessStatusCode();

        // Keycloak yeni kullanıcının kimliğini Location başlığında döndürür.
        var userId = resp.Headers.Location?.Segments.LastOrDefault()?.Trim('/')
                     ?? throw new InvalidOperationException("Kullanıcı oluşturuldu ancak kimliği alınamadı.");

        await SetRoleAsync(userId, request.Role, ct);

        var created = await GetUserAsync(userId, ct)
                      ?? throw new InvalidOperationException("Kullanıcı oluşturuldu ancak okunamadı.");

        // Keycloak 24+ "declarative user profile": tanımlanmamış öznitelikler SESSİZCE düşer.
        // tenant_id kaybolursa kullanıcı giriş yapar ama hiçbir mağaza kapsamı olmaz — erken yakala.
        if (created.StoreId != storeId)
        {
            _logger.LogError("tenant_id özniteliği kalıcı olmadı: kullanıcı={Username} beklenen={Expected} okunan={Actual}",
                request.Username, storeId, created.StoreId);
            throw new InvalidOperationException(
                "Kullanıcı oluşturuldu ancak mağaza kimliği (tenant_id) kaydedilemedi. " +
                "Keycloak realm'inde 'tenant_id' kullanıcı profili özniteliği tanımlı olmalıdır.");
        }

        _logger.LogInformation("Panel kullanıcısı oluşturuldu: {Username} ({Role}) mağaza={Store}",
            request.Username, request.Role, storeId);
        return created;
    }

    public async Task SetRoleAsync(string userId, string role, CancellationToken ct)
    {
        if (!IsAssignableRole(role))
            throw new InvalidOperationException(
                $"Atanamayan rol: '{role}'. Geçerli roller: {string.Join(", ", AssignableRoles)}");

        // Önce atanabilir rollerden mevcut olanları kaldır (tek rol modeli), sonra yenisini ekle.
        var current = await GetRealmRolesAsync(userId, ct);
        var toRemove = current.Where(r => IsAssignableRole(r)).ToList();
        if (toRemove.Count > 0)
        {
            var reps = new List<object>();
            foreach (var r in toRemove) reps.Add(await GetRoleRepresentationAsync(r, ct));
            using var del = await AuthorizedAsync(HttpMethod.Delete, $"{AdminBase}/users/{userId}/role-mappings/realm", ct);
            del.Content = JsonContent.Create(reps);
            using var delResp = await _http.SendAsync(del, ct);
            delResp.EnsureSuccessStatusCode();
        }

        var newRole = await GetRoleRepresentationAsync(role, ct);
        using var add = await AuthorizedAsync(HttpMethod.Post, $"{AdminBase}/users/{userId}/role-mappings/realm", ct);
        add.Content = JsonContent.Create(new[] { newRole });
        using var addResp = await _http.SendAsync(add, ct);
        addResp.EnsureSuccessStatusCode();
    }

    public async Task SetEnabledAsync(string userId, bool enabled, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Put, $"{AdminBase}/users/{userId}", ct);
        req.Content = JsonContent.Create(new { enabled });
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task SendPasswordResetAsync(string userId, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Put, $"{AdminBase}/users/{userId}/execute-actions-email", ct);
        req.Content = JsonContent.Create(new[] { "UPDATE_PASSWORD" });
        using var resp = await _http.SendAsync(req, ct);
        // SMTP yapılandırılmadıysa Keycloak hata döndürür; bunu çağırana açıkça bildiriyoruz (J-09 kapsamı).
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "Şifre sıfırlama e-postası gönderilemedi. Keycloak SMTP yapılandırması gerekli.");
    }

    public async Task<bool> ExistsAsync(string username, string? email, CancellationToken ct)
    {
        if (await AnyUserAsync($"{AdminBase}/users?username={Uri.EscapeDataString(username)}&exact=true", ct))
            return true;

        return !string.IsNullOrWhiteSpace(email)
            && await AnyUserAsync($"{AdminBase}/users?email={Uri.EscapeDataString(email)}&exact=true", ct);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Delete, $"{AdminBase}/users/{userId}", ct);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
            _logger.LogWarning("Kullanıcı silinemedi (telafi): {UserId} durum={Status}", userId, resp.StatusCode);
    }

    private async Task<bool> AnyUserAsync(string url, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, url, ct);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetArrayLength() > 0;
    }

    // --- Yardımcılar ---

    private async Task<PanelUser> ToPanelUserAsync(JsonElement el, string userId, CancellationToken ct)
    {
        Guid? storeId = null;
        if (el.TryGetProperty("attributes", out var attrs) &&
            attrs.ValueKind == JsonValueKind.Object &&
            attrs.TryGetProperty("tenant_id", out var t) &&
            t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0 &&
            Guid.TryParse(t[0].GetString(), out var parsed))
        {
            storeId = parsed;
        }

        var roles = (await GetRealmRolesAsync(userId, ct)).Where(IsAssignableRole).ToList();

        return new PanelUser(
            userId,
            el.GetProperty("username").GetString() ?? "",
            el.TryGetProperty("email", out var em) ? em.GetString() : null,
            el.TryGetProperty("firstName", out var fn) ? fn.GetString() : null,
            el.TryGetProperty("lastName", out var ln) ? ln.GetString() : null,
            el.TryGetProperty("enabled", out var en) && en.GetBoolean(),
            storeId,
            roles);
    }

    private async Task<IReadOnlyList<string>> GetRealmRolesAsync(string userId, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, $"{AdminBase}/users/{userId}/role-mappings/realm", ct);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("name").GetString() ?? "")
            .Where(n => n.Length > 0).ToList();
    }

    private async Task<object> GetRoleRepresentationAsync(string role, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, $"{AdminBase}/roles/{role}", ct);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new { id = doc.RootElement.GetProperty("id").GetString(), name = doc.RootElement.GetProperty("name").GetString() };
    }
}
