using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Taxi.Domain.Identity;

namespace Taxi.Application.Tests.Identity;

internal static class IdentityMocks
{
    public static Mock<UserManager<ApplicationUser>> UserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
