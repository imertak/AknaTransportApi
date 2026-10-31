using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Users;

public sealed class UserService(
    IRepository<User> users,
    IRepository<RefreshToken> refreshTokens,
    IRepository<OutboxMessage> outbox,
    IUnitOfWork unitOfWork,
    ISmsService smsService,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserService
{
    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var q = users.QueryAsNoTracking();
        if (query.Role is { } role) q = q.Where(u => u.Role == role);
        if (query.IsActive is { } active) q = q.Where(u => u.IsActive == active);
        if (query.Search.TrimOrNull() is { } search)
        {
            var term = search.ToLower();
            q = q.Where(u => u.FullName.ToLower().Contains(term) || u.NormalizedEmail.Contains(term) || u.PhoneNumber.Contains(term));
        }

        q = (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("createdat", true) => q.OrderByDescending(u => u.CreatedAt),
            ("createdat", false) => q.OrderBy(u => u.CreatedAt),
            ("fullname", true) => q.OrderByDescending(u => u.FullName),
            _ => q.OrderBy(u => u.FullName),
        };
        return await q.Select(UserMappings.Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin && currentUser.UserId != id) throw new ForbiddenException();
        return await users.QueryAsNoTracking().Where(u => u.Id == id).Select(UserMappings.Projection).FirstOrDefaultAsync(ct)
               ?? throw NotFoundException.For("Kullanıcı");
    }

    public async Task<UserCredentialResult> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException("Yalnızca yöneticiler kullanıcı ekleyebilir.");
        var normalized = User.NormalizeEmail(request.Email);
        if (await users.QueryAsNoTracking().AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("Bu e-posta adresiyle kayıtlı bir kullanıcı zaten var.");

        var password = TemporaryPassword.Generate();
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            PhoneNumber = request.PhoneNumber.Trim(),
            PasswordHash = passwordHasher.Hash(password),
            Role = request.Role,
            IsActive = true,
            MustChangePassword = true,
        };
        users.Add(user);
        var smsQueued = QueueCredentialsSms(user, password, isReset: false);
        await unitOfWork.SaveChangesAsync(ct);
        return BuildResult(user, password, smsQueued, "Kullanıcı oluşturuldu.");
    }

    public async Task<UserCredentialResult> ResetPasswordAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
        var user = await users.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Kullanıcı");
        if (!user.IsActive) throw new ConflictException("Pasif kullanıcının şifresi sıfırlanamaz.");

        var password = TemporaryPassword.Generate();
        user.PasswordHash = passwordHasher.Hash(password);
        user.MustChangePassword = true;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        var now = clock.GetUtcNow().UtcDateTime;
        var active = await refreshTokens.Query().Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active) token.Revoke(now, "Yönetici şifreyi sıfırladı");

        var smsQueued = QueueCredentialsSms(user, password, isReset: true);
        await unitOfWork.SaveChangesAsync(ct);
        return BuildResult(user, password, smsQueued, "Şifre sıfırlandı.");
    }

    /// <summary>
    /// Geçici şifre SMS'i kullanıcı kaydıyla aynı transaction'da outbox'a yazılır.
    /// SMS sağlayıcısı yapılandırılmamışsa kuyruğa alınmaz.
    /// </summary>
    private bool QueueCredentialsSms(User user, string password, bool isReset)
    {
        if (!smsService.IsConfigured) return false;
        OutboxWriter.Enqueue(outbox, OutboxMessageTypes.UserCredentialsSms,
            new UserCredentialsSmsPayload(user.Id, user.PhoneNumber, user.FullName, user.Email, password, isReset),
            clock.GetUtcNow().UtcDateTime);
        return true;
    }

    private static UserCredentialResult BuildResult(User user, string password, bool smsQueued, string prefix) => smsQueued
        ? new UserCredentialResult(user.ToDto(), true, null, $"{prefix} Geçici şifre {user.PhoneNumber} numarasına SMS ile gönderilecek.")
        : new UserCredentialResult(user.ToDto(), false, password,
            $"{prefix} SMS sağlayıcısı yapılandırılmadığı için geçici şifre yalnızca bu yanıtta gösteriliyor; kullanıcıya güvenli bir kanaldan iletin.");

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Kullanıcı");
        var normalized = User.NormalizeEmail(request.Email);
        if (normalized != user.NormalizedEmail && await users.QueryAsNoTracking().AnyAsync(u => u.NormalizedEmail == normalized && u.Id != id, ct))
            throw new ConflictException("Bu e-posta adresiyle kayıtlı bir kullanıcı zaten var.");

        if (user.Id == currentUser.UserId && (request.Role != UserRole.Admin || !request.IsActive))
            throw new ConflictException("Kendi yönetici yetkinizi kaldıramaz veya hesabınızı pasife alamazsınız.");

        if (user.Role == UserRole.Admin && (request.Role != UserRole.Admin || !request.IsActive))
        {
            var otherActiveAdmins = await users.QueryAsNoTracking().CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != id, ct);
            if (otherActiveAdmins == 0) throw new ConflictException("Sistemde en az bir aktif yönetici bulunmalıdır.");
        }

        var deactivated = user.IsActive && !request.IsActive;
        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim();
        user.NormalizedEmail = normalized;
        user.PhoneNumber = request.PhoneNumber.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;

        if (deactivated)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var active = await refreshTokens.Query().Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
            foreach (var token in active) token.Revoke(now, "Kullanıcı pasife alındı");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return user.ToDto();
    }
}
