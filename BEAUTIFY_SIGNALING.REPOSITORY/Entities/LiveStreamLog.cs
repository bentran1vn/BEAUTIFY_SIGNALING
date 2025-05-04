using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Aggregates;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Entities;

namespace BEAUTIFY_SIGNALING.REPOSITORY.Entities;

public class LiveStreamLog : AggregateRoot<Guid>, IAuditableEntity
{
    public Guid? UserId { get; set; }
    public virtual User? User { get; set; }
    public int ActivityType { get; set; }
    // 0: Join Stream
    // 1: Send Message
    // 2: Reaction
    
    public string? Message { get; set; }
        // 1: { emoji: "👍", text: "Looks great!" },
        // 2: { emoji: "❤️", text: "Love it!" },
        // 3: { emoji: "🔥", text: "That's fire!" },
        // 4: { emoji: "👏", text: "Amazing work!" },
        // 5: { emoji: "😍", text: "Beautiful!" },
        // For Reaction
    
    public Guid LivestreamRoomId { get; set; }
    public virtual LivestreamRoom LivestreamRoom { get; set; } = default!;
    
    public DateTimeOffset CreatedOnUtc { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}