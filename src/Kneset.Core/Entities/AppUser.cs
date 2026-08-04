using Microsoft.AspNetCore.Identity;

namespace Kneset.Core.Entities;

/// <summary>Пользователь платформы (ASP.NET Identity).</summary>
public class AppUser : IdentityUser
{
    /// <summary>Публичное имя — показывается как автор инициатив.</summary>
    public string? DisplayName { get; set; }

    public List<CitizenInitiative> Initiatives { get; set; } = [];
}
