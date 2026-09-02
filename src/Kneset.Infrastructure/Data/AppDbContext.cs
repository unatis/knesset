using Kneset.Core.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Ключи, которыми ASP.NET Core шифрует cookie аутентификации, антифорджери-токены
    /// и ссылки для сброса пароля. В контейнере файловая система эфемерная, поэтому
    /// связка ключей живёт в базе: иначе после каждого перезапуска все пользователи
    /// оказываются разлогинены, а открытые формы перестают отправляться.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<BillInitiator> BillInitiators => Set<BillInitiator>();
    public DbSet<BillAnalysis> BillAnalyses => Set<BillAnalysis>();
    public DbSet<BillSession> BillSessions => Set<BillSession>();
    public DbSet<BillDocument> BillDocuments => Set<BillDocument>();
    public DbSet<BillDocumentText> BillDocumentTexts => Set<BillDocumentText>();
    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<BillTitle> BillTitles => Set<BillTitle>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<BillContextAnalysis> BillContextAnalyses => Set<BillContextAnalysis>();
    public DbSet<BillReaction> BillReactions => Set<BillReaction>();
    public DbSet<CitizenInitiative> CitizenInitiatives => Set<CitizenInitiative>();
    public DbSet<InitiativeSignature> InitiativeSignatures => Set<InitiativeSignature>();
    public DbSet<InitiativeReaction> InitiativeReactions => Set<InitiativeReaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<UiTranslation> UiTranslations => Set<UiTranslation>();
    public DbSet<IsraelLaw> IsraelLaws => Set<IsraelLaw>();
    public DbSet<LawAct> LawActs => Set<LawAct>();
    public DbSet<LawAmendment> LawAmendments => Set<LawAmendment>();
    public DbSet<NotificationSubscription> NotificationSubscriptions => Set<NotificationSubscription>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserNotificationChannel> UserNotificationChannels => Set<UserNotificationChannel>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

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
            // Отбор законов по депутату идёт в обратную сторону, и составной
            // индекс выше для этого не годится: ведущий столбец в нём BillId.
            bi.HasIndex(x => x.PersonId);
            bi.HasOne(x => x.Bill).WithMany(x => x.Initiators).HasForeignKey(x => x.BillId);
            bi.HasOne(x => x.Person).WithMany(x => x.InitiatedBills).HasForeignKey(x => x.PersonId);
        });

        modelBuilder.Entity<BillSession>(s =>
        {
            // Одно заседание — один пункт повестки по этому закону.
            s.HasIndex(x => new { x.BillId, x.Kind, x.KnessetSessionId }).IsUnique();
            // Хронология закона: история стадий читается этим индексом.
            s.HasIndex(x => new { x.BillId, x.StartDate });
            // Ближайшие заседания по всей базе — для сроков в столбце контекста.
            s.HasIndex(x => x.StartDate);
            s.HasOne(x => x.Bill).WithMany(b => b.Sessions)
             .HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillTitle>(t =>
        {
            // Один перевод на язык. Иврит здесь не хранится — он в Bill.Name.
            t.HasIndex(x => new { x.BillId, x.LanguageCode }).IsUnique();
            t.Property(x => x.LanguageCode).HasMaxLength(8);
            t.Property(x => x.Text).HasMaxLength(2000);
            t.Property(x => x.SourceName).HasMaxLength(2000);
            t.HasOne(x => x.Bill).WithMany(b => b.Titles)
             .HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Committee>(c =>
        {
            // Ключ приходит из источника, генерировать свой нечего.
            c.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<BillDocument>(d =>
        {
            // Один документ — одна строка на формат: DOC и PDF делят
            // DocumentBillID, и без формата ключ был бы неуникальным.
            d.HasIndex(x => new { x.BillId, x.KnessetDocumentId, x.Format }).IsUnique();
            d.HasOne(x => x.Bill).WithMany(b => b.Documents)
             .HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillDocumentText>(t =>
        {
            // Один текст на документ. Переразбор обновляет строку, а не
            // добавляет новую: история текста не нужна, нужен актуальный.
            t.HasIndex(x => x.BillDocumentId).IsUnique();
            // Обходчик спрашивает «что ещё не разобрано» — индекс по статусу
            // и версии парсера делает этот запрос дешёвым.
            t.HasIndex(x => new { x.Status, x.ExtractorVersion });
            t.Property(x => x.ExtractorVersion).HasMaxLength(50);
            t.Property(x => x.SourceHash).HasMaxLength(64);
            t.Property(x => x.Status).HasMaxLength(20);
            t.Property(x => x.Error).HasMaxLength(500);
            t.HasOne(x => x.BillDocument).WithOne(d => d.ExtractedText)
             .HasForeignKey<BillDocumentText>(x => x.BillDocumentId)
             .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<IsraelLaw>(l =>
        {
            l.HasIndex(x => x.KnessetIsraelLawId).IsUnique();
            l.HasIndex(x => x.IsBasicLaw);
            l.Property(x => x.Name).HasMaxLength(2000);
            l.Property(x => x.ValidityDesc).HasMaxLength(200);
        });

        modelBuilder.Entity<LawAct>(a =>
        {
            a.HasIndex(x => x.KnessetLawId).IsUnique();
            a.Property(x => x.Name).HasMaxLength(2000);
        });

        modelBuilder.Entity<LawAmendment>(a =>
        {
            a.HasIndex(x => x.KnessetBindingId).IsUnique();
            // Выборка поправок конкретного закона — основной запрос на карточке.
            a.HasIndex(x => new { x.IsraelLawId, x.IsIndirect });
            a.Property(x => x.ActName).HasMaxLength(2000);
            a.Property(x => x.BindingTypeDesc).HasMaxLength(200);
            a.Property(x => x.AmendmentTypeDesc).HasMaxLength(200);
            a.HasOne(x => x.IsraelLaw).WithMany(x => x.Amendments)
                .HasForeignKey(x => x.IsraelLawId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUser>(u =>
        {
            u.Property(x => x.PreferredLanguage).HasMaxLength(10).HasDefaultValue("ru");
        });

        modelBuilder.Entity<NotificationSubscription>(s =>
        {
            s.Property(x => x.Keyword).HasMaxLength(200);
            s.Property(x => x.TargetKey).HasMaxLength(250);
            s.HasIndex(x => new { x.UserId, x.Kind, x.TargetKey }).IsUnique();
            // Рассылка идёт от законопроекта к подписчикам, поэтому нужен обратный поиск.
            s.HasIndex(x => new { x.Kind, x.PersonId });
            s.HasIndex(x => new { x.Kind, x.BillId });
            s.HasOne(x => x.User).WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            s.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId);
            s.HasOne(x => x.Bill).WithMany().HasForeignKey(x => x.BillId);
        });

        modelBuilder.Entity<Notification>(n =>
        {
            n.Property(x => x.TriggerDetail).HasMaxLength(500);
            n.HasIndex(x => new { x.UserId, x.ReadAt });
            n.HasIndex(x => new { x.UserId, x.CreatedAt });
            // Защита от повторной вставки, если рассылку прервали и перезапустили.
            // EventAt в ключе: вторая смена стадии у того же закона — это новое событие.
            n.HasIndex(x => new { x.UserId, x.BillId, x.Kind, x.EventAt }).IsUnique();
            n.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            n.HasOne(x => x.Bill).WithMany().HasForeignKey(x => x.BillId);
        });

        modelBuilder.Entity<UserNotificationChannel>(c =>
        {
            c.Property(x => x.Address).HasMaxLength(300);
            c.HasIndex(x => new { x.UserId, x.Channel }).IsUnique();
            c.HasOne(x => x.User).WithMany(x => x.NotificationChannels)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationDelivery>(d =>
        {
            d.Property(x => x.Error).HasMaxLength(1000);
            d.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
            d.HasOne(x => x.Notification).WithMany(x => x.Deliveries)
                .HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
