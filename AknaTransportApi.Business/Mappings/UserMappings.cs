using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

public static class UserMappings
{
    public static readonly Expression<Func<User, UserDto>> Projection = u =>
        new UserDto(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive, u.MustChangePassword, u.CreatedAt, u.UpdatedAt);

    private static readonly Func<User, UserDto> Compiled = Projection.Compile();

    public static UserDto ToDto(this User user) => Compiled(user);
}
