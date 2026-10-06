using LanguageLab.Api.Auth;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Tests;

public class AdminAuditTests
{
    [Fact]
    public void An_admin_write_is_logged_with_actor_target_and_outcome()
    {
        var logger = new ListLogger<AdminAuditTests>();

        AdminAudit.Write(logger, 1, "POST", "/api/admin/users/5/ban", 204);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Admin 1 POST /api/admin/users/5/ban -> 204", entry.Message);
    }
}
