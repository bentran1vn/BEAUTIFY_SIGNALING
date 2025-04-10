using System.ComponentModel.DataAnnotations;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;
public class User : AggregateRoot<Guid>, IAuditableEntity
{
    [RegularExpression(@"^([\w\.\-]+)@([\w\-]+)(\.[a-zA-Z]{2,})$", ErrorMessage = "Invalid Email Format")]
    [MaxLength(100)]
    [Required]
    public required string Email { get; init; }

    [MaxLength(50)] public required string FirstName { get; set; }
    [MaxLength(50)] public required string LastName { get; set; }
    [MaxLength(255)] public required string Password { get; set; }

    [MaxLength(50)] public required int Status { get; set; }
    // 0 Pending 1 Approve 2 Reject 3 Banned
    public string? FullName => $"{FirstName} {LastName}".Trim();
    public DateOnly? DateOfBirth { get; set; }
    public Guid? RoleId { get; set; }
    public virtual Role? Role { get; set; }

    [MaxLength(14, ErrorMessage = "Phone Number must be 10 digits")]
    public string? PhoneNumber { get; set; }

    [MaxLength(250)] public string? ProfilePicture { get; set; }

    // address in detail
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? District { get; set; }
    [MaxLength(100)] public string? Ward { get; set; }
    [MaxLength(100)] public string? Address { get; set; }

    [MaxLength(250)] public string? FullAddress => $"{Address}, {Ward}, {District}, {City}".Trim(',', ' ', '\n');


    [MaxLength(250)] public string? RefreshToken { get; set; }


    public virtual ICollection<UserConversation>? UserConversations { get; set; }
    // public virtual ICollection<CustomerSchedule>? CustomerSchedules { get; set; }
    // public virtual ICollection<Order>? Orders { get; set; }
    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}