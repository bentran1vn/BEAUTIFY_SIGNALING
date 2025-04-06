using System.ComponentModel.DataAnnotations;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;
public class Promotion : AggregateRoot<Guid>, IAuditableEntity
{
    [MaxLength(100)] public required string Name { get; set; }
    [MaxLength(250)] public string? ImageUrl { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    [MaxLength(50)] public string? Status { get; set; }
    public double DiscountPercent { get; set; }
    public Guid? ServiceId { get; set; }
    public virtual Service? Service { get; set; }
    public bool IsActivated { get; set; } = false;
    public Guid? LivestreamRoomId { get; set; }
    public virtual LivestreamRoom? LivestreamRoom { get; set; }
    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}