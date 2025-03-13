using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using Microsoft.EntityFrameworkCore;

namespace BEAUTIFY_SIGNALING.REPOSITORY;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinic { get; set; }
    public DbSet<LivestreamRoom> LivestreamRoom { get; set; }
}