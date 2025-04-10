using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;
public class UserConversation : AggregateRoot<Guid>, IAuditableEntity
{
    public Guid? UserId { get; set; }
    public virtual User? User { get; set; } = null!;

    public Guid? ClinicId { get; set; }
    public virtual Clinic? Clinic { get; set; } = null!;

    public Guid ConversationId { get; set; }
    public virtual Conversation Conversation { get; set; } = null!;

    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}