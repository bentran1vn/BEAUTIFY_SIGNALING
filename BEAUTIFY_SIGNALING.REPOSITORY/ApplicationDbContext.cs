using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using Microsoft.EntityFrameworkCore;

namespace BEAUTIFY_SIGNALING.REPOSITORY;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinic { get; set; }
    public DbSet<LivestreamRoom> LivestreamRoom { get; set; }
    public DbSet<Category> Category { get; set; }
    public DbSet<ClinicService> ClinicService { get; set; }
    public DbSet<Promotion> Promotion { get; set; }
    public DbSet<Role> Role { get; set; }
    public DbSet<Service> Service { get; set; }
    public DbSet<ServiceMedia> ServiceMedia { get; set; }
    public DbSet<Staff> Staff { get; set; }
    public DbSet<User> User { get; set; }
    public DbSet<UserClinic> UserClinic { get; set; }
    public DbSet<Conversation> Conversation { get; set; }
    public DbSet<UserConversation> UserConversation { get; set; }
    public DbSet<Message> Message { get; set; }
    public DbSet<Order> Order { get; set; }
    public DbSet<LiveStreamDetail> LiveStreamDetail { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the relationship between LivestreamRoom and LiveStreamDetail
        modelBuilder.Entity<LivestreamRoom>()
            .HasOne(lr => lr.LiveStreamDetail)
            .WithOne()  // Assuming one-to-one relationship, adjust if it's one-to-many
            .HasForeignKey<LivestreamRoom>(lr => lr.LiveStreamDetailId)
            .OnDelete(DeleteBehavior.Restrict);  // Adjust delete behavior as needed
    }
}