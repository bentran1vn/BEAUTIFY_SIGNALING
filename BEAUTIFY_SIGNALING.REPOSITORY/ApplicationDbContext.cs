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
}