using System.ComponentModel.DataAnnotations;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;
public class Message : AggregateRoot<Guid>, IAuditableEntity
{
    public Guid? ConversationId { get; set; }
    public virtual Conversation? Conversation { get; set; }
    public Guid SenderId { get; set; }
    //public virtual User? Sender { get; set; }

    public bool IsClinic { get; set; } = false;
    [MaxLength(200)] public required string Content { get; set; }
    public bool IsRead { get; set; } = false;
    public Guid? LivestreamRoomId { get; set; }
    public virtual LivestreamRoom? LivestreamRoom { get; set; }

    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}