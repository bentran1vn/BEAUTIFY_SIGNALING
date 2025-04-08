using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;
public class Order : AggregateRoot<Guid>, IAuditableEntity
{
    public Guid CustomerId { get; set; }
    public virtual User? Customer { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Discount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? FinalAmount { get; set; }
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LivestreamRoomId {get;set;}
    public Guid? ServiceId { get; set; }
    public virtual Service? Service { get; set; }
    [MaxLength(50)] public string? Status { get; set; }
    // public virtual ICollection<OrderDetail>? OrderDetails { get; set; } = [];
    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}