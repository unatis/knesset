using Kneset.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<BillInitiator> BillInitiators => Set<BillInitiator>();
    public DbSet<BillAnalysis> BillAnalyses => Set<BillAnalysis>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<BillContextAnalysis> BillContextAnalyses => Set<BillContextAnalysis>();
    public DbSet<BillReaction> BillReactions => Set<BillReaction>();
    public DbSet<CitizenInitiative> CitizenInitiatives => Set<CitizenInitiative>();
    public DbSet<InitiativeSignature> InitiativeSignatures => Set<InitiativeSignature>();
    public DbSet<InitiativeReaction> InitiativeReactions => Set<InitiativeReaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<UiTranslation> UiTranslations => Set<UiTranslation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Bill>(b =>
        {
            b.HasIndex(x => x.KnessetBillId).IsUnique();
            b.HasIndex(x => x.KnessetNum);
            b.HasIndex(x => x.LastUpdatedDate);
            b.Property(x => x.Name).HasMaxLength(2000);
            b.Property(x => x.NameRu).HasMaxLength(2000);
            b.Property(x => x.StatusDesc).HasMaxLength(500);
            b.Property(x => x.SubTypeDesc).HasMaxLength(500);
        });

        modelBuilder.Entity<Person>(p =>
        {
            p.HasIndex(x => x.KnessetPersonId).IsUnique();
            p.Property(x => x.FirstName).HasMaxLength(200);
            p.Property(x => x.LastName).HasMaxLength(200);
            p.Property(x => x.FactionName).HasMaxLength(500);
            p.Property(x => x.PhotoUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<BillInitiator>(bi =>
        {
            bi.HasIndex(x => new { x.BillId, x.PersonId }).IsUnique();
            bi.HasOne(x => x.Bill).WithMany(x => x.Initiators).HasForeignKey(x => x.BillId);
            bi.HasOne(x => x.Person).WithMany(x => x.InitiatedBills).HasForeignKey(x => x.PersonId);
        });

        modelBuilder.Entity<BillAnalysis>(a =>
        {
            a.HasIndex(x => new { x.BillId, x.IsStale });
            a.Property(x => x.AnalysisJson).HasColumnType("jsonb");
            a.Property(x => x.ModelVersion).HasMaxLength(200);
            a.Property(x => x.LanguageCode).HasMaxLength(10);
            a.HasOne(x => x.Bill).WithMany(x => x.Analyses).HasForeignKey(x => x.BillId);
        });

        modelBuilder.Entity<SyncLog>(s =>
        {
            s.Property(x => x.EntityName).HasMaxLength(100);
            s.HasIndex(x => new { x.EntityName, x.StartedUtc });
        });

        modelBuilder.Entity<BillReaction>(r =>
        {
            r.HasIndex(x => new { x.BillId, x.UserId }).IsUnique();
            r.HasIndex(x => new { x.BillId, x.Kind });
            r.HasOne(x => x.Bill).WithMany(x => x.Reactions).HasForeignKey(x => x.BillId);
            r.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillContextAnalysis>(c =>
        {
            c.HasIndex(x => new { x.BillId, x.IsStale });
            c.Property(x => x.ContextJson).HasColumnType("jsonb");
            c.Property(x => x.ModelVersion).HasMaxLength(200);
            c.Property(x => x.LanguageCode).HasMaxLength(10);
            c.HasOne(x => x.Bill).WithMany().HasForeignKey(x => x.BillId);
        });

        modelBuilder.Entity<CitizenInitiative>(ci =>
        {
            ci.Property(x => x.Title).HasMaxLength(300);
            ci.Property(x => x.StructuredJson).HasColumnType("jsonb");
            ci.Property(x => x.ModelVersion).HasMaxLength(200);
            ci.HasIndex(x => x.Status);
            ci.HasOne(x => x.Author).WithMany(x => x.Initiatives)
                .HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InitiativeSignature>(sig =>
        {
            sig.HasIndex(x => new { x.InitiativeId, x.UserId }).IsUnique();
            sig.HasOne(x => x.Initiative).WithMany(x => x.Signatures)
                .HasForeignKey(x => x.InitiativeId);
            sig.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InitiativeReaction>(r =>
        {
            r.HasIndex(x => new { x.InitiativeId, x.UserId }).IsUnique();
            r.HasOne(x => x.Initiative).WithMany()
                .HasForeignKey(x => x.InitiativeId);
            r.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UiTranslation>(t =>
        {
            t.Property(x => x.Key).HasMaxLength(200);
            t.Property(x => x.LanguageCode).HasMaxLength(10);
            t.HasIndex(x => new { x.Key, x.LanguageCode }).IsUnique();
        });

        modelBuilder.Entity<Comment>(c =>
        {
            c.Property(x => x.Text).HasMaxLength(4000);
            c.HasIndex(x => new { x.BillId, x.CreatedAt });
            c.HasIndex(x => new { x.InitiativeId, x.CreatedAt });
            c.HasOne(x => x.Bill).WithMany().HasForeignKey(x => x.BillId);
            c.HasOne(x => x.Initiative).WithMany().HasForeignKey(x => x.InitiativeId);
            c.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
