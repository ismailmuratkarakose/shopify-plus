using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Merchant.Api.Identity;
using Marketplace.Merchant.Api.Infrastructure;

namespace Marketplace.Merchant.Api.Api;

public record InviteUserRequest(string Username, string? Email, string? FirstName, string? LastName,
    string Role, string? TemporaryPassword);
public record SetRoleRequest(string Role);

/// <summary>
/// Mağaza ekibinin yönetimi: mağaza sahibi kendi kullanıcılarını ekler ve rollerini belirler
/// (içerik editörü / yayın yöneticisi / mağaza yöneticisi).
/// Platform personeli, X-Acting-Store başlığıyla bir mağazanın kapsamına girerek aynı uçları kullanabilir.
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization(Policies.StoreManage)
            .WithTags("Users");

        // Atanabilir roller (panel arayüzü bu listeyi gösterir).
        group.MapGet("/roles", () => Results.Ok(new[]
        {
            new { role = Roles.StoreAdmin, displayName = "Mağaza Yöneticisi", description = "Mağaza ayarları, kullanıcı yönetimi ve tüm içerik işlemleri" },
            new { role = Roles.PublishManager, displayName = "Yayın Yöneticisi", description = "İçeriği onaylar, yayınlar, sürüm yönetir" },
            new { role = Roles.ContentEditor, displayName = "İçerik Editörü", description = "İçerik hazırlar ve düzenler; yayınlayamaz" }
        }));

        group.MapGet("/", async (IStoreContext scope, IKeycloakAdminClient admin, CancellationToken ct) =>
        {
            if (scope.StoreId is not { } storeId) return NoStoreScope();
            var users = await admin.GetStoreUsersAsync(storeId, ct);
            return Results.Ok(users);
        });

        group.MapPost("/", async (InviteUserRequest req, IStoreContext scope,
            IKeycloakAdminClient admin, IAuditLogger audit, MerchantDbContext db, CancellationToken ct) =>
        {
            if (scope.StoreId is not { } storeId) return NoStoreScope();
            if (string.IsNullOrWhiteSpace(req.Username))
                return Problem("Kullanıcı adı zorunludur.", StatusCodes.Status400BadRequest, "user.invalid");
            if (!KeycloakAdminClient.IsAssignableRole(req.Role))
                return Problem($"Geçersiz rol: '{req.Role}'. Geçerli roller: {string.Join(", ", KeycloakAdminClient.AssignableRoleNames)}",
                    StatusCodes.Status400BadRequest, "user.invalid_role");

            try
            {
                var created = await admin.CreateStoreUserAsync(storeId,
                    new CreatePanelUser(req.Username.Trim(), req.Email, req.FirstName, req.LastName,
                        req.Role, req.TemporaryPassword, PasswordIsTemporary: true), ct);

                audit.Record("user.invited", $"'{created.Username}' kullanıcısı '{req.Role}' rolüyle eklendi",
                    "User", created.Id);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/users/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Problem(ex.Message, StatusCodes.Status409Conflict, "user.create_failed");
            }
        });

        group.MapPut("/{userId}/role", async (string userId, SetRoleRequest req, IStoreContext scope,
            IKeycloakAdminClient admin, IAuditLogger audit, MerchantDbContext db, CancellationToken ct) =>
        {
            var check = await EnsureUserInStoreAsync(userId, scope, admin, ct);
            if (check is not null) return check;

            try
            {
                await admin.SetRoleAsync(userId, req.Role, ct);
                var updated = await admin.GetUserAsync(userId, ct);
                audit.Record("user.role_changed", $"'{updated?.Username}' kullanıcısının rolü '{req.Role}' olarak değiştirildi",
                    "User", userId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Problem(ex.Message, StatusCodes.Status400BadRequest, "user.invalid_role");
            }
        });

        group.MapPost("/{userId}/deactivate", async (string userId, IStoreContext scope,
            IKeycloakAdminClient admin, IAuditLogger audit, MerchantDbContext db, CancellationToken ct) =>
        {
            var check = await EnsureUserInStoreAsync(userId, scope, admin, ct);
            if (check is not null) return check;
            await admin.SetEnabledAsync(userId, false, ct);
            var user = await admin.GetUserAsync(userId, ct);
            audit.Record("user.deactivated", $"'{user?.Username}' kullanıcısı pasifleştirildi", "User", userId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(user);
        });

        group.MapPost("/{userId}/activate", async (string userId, IStoreContext scope,
            IKeycloakAdminClient admin, CancellationToken ct) =>
        {
            var check = await EnsureUserInStoreAsync(userId, scope, admin, ct);
            if (check is not null) return check;
            await admin.SetEnabledAsync(userId, true, ct);
            return Results.Ok(await admin.GetUserAsync(userId, ct));
        });

        group.MapPost("/{userId}/reset-password", async (string userId, IStoreContext scope,
            IKeycloakAdminClient admin, CancellationToken ct) =>
        {
            var check = await EnsureUserInStoreAsync(userId, scope, admin, ct);
            if (check is not null) return check;

            try
            {
                await admin.SendPasswordResetAsync(userId, ct);
                return Results.Ok(new { sent = true });
            }
            catch (InvalidOperationException ex)
            {
                // SMTP henüz yapılandırılmadıysa (J-09) burada net bir mesaj döner.
                return Problem(ex.Message, StatusCodes.Status503ServiceUnavailable, "user.email_unavailable");
            }
        });

        return app;
    }

    /// <summary>
    /// Hedef kullanıcının gerçekten bu mağazaya ait olduğunu doğrular —
    /// bir mağaza yöneticisinin başka mağazanın kullanıcısını değiştirmesini engeller.
    /// </summary>
    private static async Task<IResult?> EnsureUserInStoreAsync(
        string userId, IStoreContext scope, IKeycloakAdminClient admin, CancellationToken ct)
    {
        if (scope.StoreId is not { } storeId) return NoStoreScope();

        var user = await admin.GetUserAsync(userId, ct);
        if (user is null)
            return Problem($"Kullanıcı bulunamadı: {userId}", StatusCodes.Status404NotFound, "user.not_found");
        if (user.StoreId != storeId)
            return Problem("Bu kullanıcı sizin mağazanıza ait değil.", StatusCodes.Status403Forbidden, "user.foreign_store");

        return null;
    }

    private static IResult NoStoreScope() => Problem(
        "Mağaza kapsamı yok. Platform kullanıcıları X-Acting-Store başlığıyla bir mağaza seçmelidir.",
        StatusCodes.Status400BadRequest, "store.missing");

    private static IResult Problem(string detail, int status, string title) =>
        Results.Problem(detail, statusCode: status, title: title);
}
